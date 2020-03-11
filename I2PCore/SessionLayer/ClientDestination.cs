using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using I2PCore.Data;
using I2PCore.TransportLayer;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using static I2PCore.SessionLayer.RemoteDestinationsLeasesUpdates;
using static System.Configuration.ConfigurationManager;

namespace I2PCore.SessionLayer
{
    public class ClientDestination: IClient
    {
        public static readonly TickSpan InitiatorInactivityLimit = TickSpan.Minutes( 5 );

        public int LowWatermarkForNewTags { get; set; } = 7;
        public int NewTagsWhenGenerating { get; set; } = 15;

        public delegate void DestinationDataReceived( ClientDestination dest, BufLen data );
        /// <summary>
        /// Data was received by this Destination from a remote Origin.
        /// </summary>
        public event DestinationDataReceived DataReceived;

        public string Name { get; set; }

        /// <summary>
        /// New inbound tunnels was established. To be able to publish them
        /// the user of the router must sign the leases and assign to PublishedLeases
        /// as no signing key is available to the router.
        /// </summary>
        public event Action<I2PLeaseSet> SignLeasesRequest;

        public enum ClientStates { NoTunnels, NoLeases, Established }

        public event Action<ClientDestination, ClientStates> ClientStateChanged;

        /// <summary>
        /// ls is null if lookup failed.
        /// </summary>
        public delegate void DestinationLookupResult( I2PIdentHash id, I2PLeaseSet ls, object tag );

        public int InboundTunnelHopCount { get; set; } = 3;
        public int OutboundTunnelHopCount { get; set; } = 3;

        public int TargetInboundTunnelCount { get; set; } = 2;
        public int TargetOutboundTunnelCount { get; set; } = 2;

        /// <summary>
        /// Currently established Leases for this Destination.
        /// </summary>
        /// <value>The leases.</value>
        public I2PLeaseSet EstablishedLeases { get; private set; }

        /// <summary>
        /// Currently signed Leases for this Destination.
        /// </summary>
        /// <value>The leases.</value>
        public I2PLeaseSet SignedLeases 
        { 
            get => SignedLeasesField; 
            set
            {
#if DEBUG
                if ( !value.VerifySignature( Destination.SigningPublicKey ) )
                {
                    throw new ArgumentException( "Lease set signature error" );
                }
#endif

                SignedLeasesField = value;
                ThreadPool.QueueUserWorkItem( a => 
                    UpdateNetworkWithPublishedLeases() );
            }
        }
        public I2PLeaseSet SignedLeasesField = null;

        /// <summary>
        /// I2P Destination address for this instance.
        /// </summary>
        /// <value>The destination.</value>
        public I2PDestination Destination { get; private set; }

        readonly bool PublishDestination;

        public int InboundTunnelsNeeded
        {
            get
            {
                var stable = InboundEstablishedPool.Count( t => !t.Key.NeedsRecreation );
                var result = TargetInboundTunnelCount
                            - stable
                            - InboundPending.Count;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"{this}: TargetInboundTunnelCount: {TargetInboundTunnelCount} " +
                            $"Stable: {stable} Pending: {InboundPending.Count} Result: {result}" );
#endif

                return result;
            }
        }

        public int OutboundTunnelsNeeded
        {
            get
            {
                var stable = OutboundEstablishedPool.Count( t => !t.Key.NeedsRecreation );
                var result = TargetOutboundTunnelCount
                            - stable
                            - OutboundPending.Count;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"{this}: TargetOutboundTunnelCount: {TargetOutboundTunnelCount} " +
                            $"Stable: {stable} Pending: {OutboundPending.Count} Result: {result}" );
#endif

                return result;
            }
        }

        public bool ClientTunnelsStatusOk
        {
            get
            {
                return InboundEstablishedPool.Count >= TargetInboundTunnelCount
                    && OutboundEstablishedPool.Count >= TargetOutboundTunnelCount;
            }
        }

        readonly protected ConcurrentDictionary<OutboundTunnel, byte> OutboundPending =
                new ConcurrentDictionary<OutboundTunnel, byte>();
        readonly protected ConcurrentDictionary<InboundTunnel, byte> InboundPending =
                new ConcurrentDictionary<InboundTunnel, byte>();
        readonly protected ConcurrentDictionary<OutboundTunnel, byte> OutboundEstablishedPool =
                new ConcurrentDictionary<OutboundTunnel, byte>();
        readonly protected ConcurrentDictionary<InboundTunnel, byte> InboundEstablishedPool =
                new ConcurrentDictionary<InboundTunnel, byte>();

        private DecryptReceivedSessions IncommingSessions;

        protected readonly RemoteDestinationsLeasesUpdates MyRemoteDestinations;

        protected readonly ConcurrentDictionary<I2PIdentHash, SessionKeyOrigin> SessionKeys =
                new ConcurrentDictionary<I2PIdentHash, SessionKeyOrigin>();

        /// <summary>
        /// If null, the leases have to be explicitly signed by the user.
        /// </summary>
        /// <value>The this destination info.</value>
        protected I2PDestinationInfo ThisDestinationInfo { get; private set; }

        public I2PPrivateKey PrivateKeyField;
        public I2PPrivateKey PrivateKey
        {
            get => PrivateKeyField; 

            set
            {
                if ( value == PrivateKeyField ) return;

                PrivateKeyField = value;
                IncommingSessions = new DecryptReceivedSessions( this, PrivateKeyField );
            }
        }

        protected I2PSessionKey DefaultFakeSession = new I2PSessionKey();

        // Let the router sign leases
        internal ClientDestination( I2PDestinationInfo destinfo, bool publishdest )
        {
            PublishDestination = publishdest;
            ThisDestinationInfo = destinfo;
            Destination = ThisDestinationInfo.Destination;

            MyRemoteDestinations = new RemoteDestinationsLeasesUpdates( this );

            NetDb.Inst.LeaseSetUpdates += NetDb_LeaseSetUpdates;
            NetDb.Inst.IdentHashLookup.LeaseSetReceived += IdentHashLookup_LeaseSetReceived;

            IncommingSessions = new DecryptReceivedSessions( this, ThisDestinationInfo.PrivateKey );
            EstablishedLeases = new I2PLeaseSet( Destination, null, null );

            ReadAppConfig();
        }

        // Sign leases yourself
        internal ClientDestination( I2PDestination dest, I2PPrivateKey privkey, bool publishdest )
        {
            PublishDestination = publishdest;
            ThisDestinationInfo = null;
            PrivateKey = privkey;
            Destination = dest;

            MyRemoteDestinations = new RemoteDestinationsLeasesUpdates( this );

            NetDb.Inst.LeaseSetUpdates += NetDb_LeaseSetUpdates;
            NetDb.Inst.IdentHashLookup.LeaseSetReceived += IdentHashLookup_LeaseSetReceived;

            IncommingSessions = new DecryptReceivedSessions( this, privkey );
            EstablishedLeases = new I2PLeaseSet( Destination, null, null );

            ReadAppConfig();
        }

        public bool Terminated { get; protected set; } = false;

        public void Shutdown()
        {
            NetDb.Inst.IdentHashLookup.LeaseSetReceived -= IdentHashLookup_LeaseSetReceived;
            NetDb.Inst.LeaseSetUpdates -= NetDb_LeaseSetUpdates;

            Router.ShutdownClient( this );
            Terminated = true;
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( AppSettings["InboundTunnelsPerDestination"] ) )
            {
                TargetInboundTunnelCount = int.Parse( AppSettings["InboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["OutboundTunnelsPerDestination"] ) )
            {
                TargetOutboundTunnelCount = int.Parse( AppSettings["OutboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["InboundTunnelHops"] ) )
            {
                InboundTunnelHopCount = int.Parse( AppSettings["InboundTunnelHops"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["OutboundTunnelHops"] ) )
            {
                OutboundTunnelHopCount = int.Parse( AppSettings["OutboundTunnelHops"] );
            }
        }

        private ClientStates ClientStateField = ClientStates.NoTunnels;
        public ClientStates ClientState
        {
            get
            {
                return ClientStateField;
            }
            set
            {
                if ( ClientStateField == value ) return;

                ClientStateField = value;
                ClientStateChanged?.Invoke( this, ClientStateField );
            }
        }

        protected InboundTunnel SelectInboundTunnel()
        {
            return TunnelProvider.SelectTunnel<InboundTunnel>( InboundEstablishedPool.Keys );
        }

        protected OutboundTunnel SelectOutboundTunnel()
        {
            return TunnelProvider.SelectTunnel<OutboundTunnel>( OutboundEstablishedPool.Keys );
        }

        protected I2PLease SelectLease( I2PLeaseSet ls )
        {
            return ls
                    .Leases
                    .OrderByDescending( l => (ulong)l.EndDate )
                    .Take( 2 )
                    .Random();
        }

        public void AddOutboundPending( OutboundTunnel tunnel )
        {
            OutboundPending[tunnel] = 0;
        }

        public void AddInboundPending( InboundTunnel tunnel )
        {
            InboundPending[tunnel] = 0;
        }

        public void TunnelEstablished( Tunnel tunnel )
        {
            RemovePendingTunnel( tunnel );

            if ( tunnel is OutboundTunnel ot )
            {
                OutboundEstablishedPool[ot] = 0;
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundEstablishedPool[it] = 0;
                it.GarlicMessageReceived += InboundTunnel_GarlicMessageReceived;
                AddTunnelToEstablishedLeaseSet( it );
            }

            UpdateClientState();
        }

        public void RemoveTunnel( Tunnel tunnel )
        {
            RemovePendingTunnel( tunnel );
            RemovePoolTunnel( tunnel );

            UpdateClientState();
        }

        PeriodicAction KeepRemotesUpdated = new PeriodicAction( TickSpan.Seconds( 10 ) );
        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 15 ) );

        public virtual void Execute()
        {
            if ( Terminated ) return;

            KeepRemotesUpdated.Do( () =>
            {
                if ( MyRemoteDestinations.LastUpdate.DeltaToNow > TickSpan.Minutes( 5 ) )
                {
                    UpdateNetworkWithPublishedLeases();
                    UpdateClientState();
                }
            } );

            QueueStatusLog.Do( () =>
            {
                Logging.LogInformation(
                    $"{this}: Established tunnels in: {InboundEstablishedPool.Count,2}, " +
                    $"out: {OutboundEstablishedPool.Count,2}. " +
                    $"Pending in: {InboundPending.Count,2}, out {OutboundPending.Count,2}" );
            } );
        }

        void RemovePendingTunnel( Tunnel tunnel )
        {
            if ( tunnel is OutboundTunnel ot )
            {
                OutboundPending.TryRemove( ot, out _ );
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundPending.TryRemove( it, out _ );
            }
        }

        void RemovePoolTunnel( Tunnel tunnel )
        {
            if ( tunnel is OutboundTunnel ot )
            {
                OutboundEstablishedPool.TryRemove( ot, out _ );
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundEstablishedPool.TryRemove( it, out _ );
                RemoveTunnelFromEstablishedLeaseSet( (InboundTunnel)tunnel );
            }
        }

        void InboundTunnel_GarlicMessageReceived( GarlicMessage msg )
        {
            try
            {
                /*
                var info = FreenetBase64.Encode( new BufLen( ThisDestinationInfo.ToByteArray() ) );
                var g = FreenetBase64.Encode( new BufLen( msg.Garlic.ToByteArray() ) );
                var st = $"DestinationInfo:\r\n{string.Join( "\r\n", info.Chunks( 40 ).Select( s => $"\"{s}\" +" ) )}" +
                    $"\r\nGarlic (EG):\r\n{string.Join( "\r\n", g.Chunks( 40 ).Select( s => $"\"{s}\" +" ) )}";
                File.WriteAllText( $"Garlic_{DateTime.Now.ToLongTimeString()}.b64", st );
                */

                var decr = IncommingSessions.DecryptMessage( msg.Garlic );
                if ( decr == null )
                {
                    Logging.LogWarning( $"{this}: GarlicMessageReceived: Failed to decrypt garlic." );
                    return;
                }

#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this}: GarlicMessageReceived: {decr}" );
#endif

                foreach ( var clove in decr.Cloves )
                {
                    try
                    {
                        switch ( clove.Delivery.Delivery )
                        {
                            case GarlicCloveDelivery.DeliveryMethod.Local:
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: Delivered Local: {clove.Message}" );
#endif
                                TunnelProvider.Inst.DistributeIncomingMessage( null, clove.Message.CreateHeader16 );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Router:
                                var dest = ( (GarlicCloveDeliveryRouter)clove.Delivery ).Destination;
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: Delivered Router: {dest.Id32Short} {clove.Message}" );
#endif
                                TransportProvider.Send( dest, clove.Message );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Tunnel:
                                var tone = (GarlicCloveDeliveryTunnel)clove.Delivery;
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: " +
                                    $"Delivered Tunnel: {tone.Destination.Id32Short} " +
                                    $"TunnelId: {tone.Tunnel} {clove.Message}" );
#endif
                                TransportProvider.Send(
                                        tone.Destination,
                                        new TunnelGatewayMessage(
                                            clove.Message,
                                            tone.Tunnel ) );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Destination:
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: " +
                                    $"Delivered Destination: {clove.Message}" );
#endif
                                if ( clove.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore )
                                {
                                    var dbsmsg = (DatabaseStoreMessage)clove.Message;

                                    if ( dbsmsg?.LeaseSet is null )
                                    {
                                        break;
                                    }

                                    if ( dbsmsg?.LeaseSet?.Destination.IdentHash == Destination.IdentHash )
                                    {
                                        // that is me
                                        break;
                                    }

                                    if ( dbsmsg.LeaseSet != null )
                                    {
                                        Logging.LogDebug( $"{this}: New lease set received in stream for {dbsmsg.LeaseSet.Destination}." );
                                        NetDb.Inst.AddLeaseSet( dbsmsg.LeaseSet );
                                        MyRemoteDestinations.LeaseSetReceived( 
                                            dbsmsg.LeaseSet );
                                        UpdateClientState();
                                    }
                                }

                                DestinationMessageReceived( clove.Message );
                                break;
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( "ClientDestination GarlicDecrypt Clove", ex );
                    }
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( "ClientDestination GarlicDecrypt", ex );
            }
        }

        internal void DestinationMessageReceived( I2NPMessage message )
        {
#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this}: DestinationMessageReceived: {message}" );
#endif
            if ( message.MessageType == I2NPMessage.MessageTypes.Data && DataReceived != null )
            {
                var dmsg = (DataMessage)message;
                DataReceived( this, dmsg.DataMessagePayload );
            }
        }

        internal virtual void AddTunnelToEstablishedLeaseSet( InboundTunnel tunnel )
        {
            EstablishedLeases.AddLease(
                new I2PLease(
                    tunnel.Destination,
                    tunnel.GatewayTunnelId ) );

            if ( ThisDestinationInfo is null )
            {
                if ( InboundTunnelsNeeded <= 0 )
                {
                    try
                    {
                        SignLeasesRequest?.Invoke( EstablishedLeases );
                    }
                    catch ( Exception ex )
                    {
                        Logging.LogDebug( ex );
                    }
                }
            }
            else
            {
                // Auto sign
                SignedLeases = new I2PLeaseSet(
                    Destination, 
                    EstablishedLeases.Leases, 
                    new I2PLeaseInfo( ThisDestinationInfo ) );
            }
        }

        internal virtual void RemoveTunnelFromEstablishedLeaseSet( InboundTunnel tunnel )
        {
            EstablishedLeases.RemoveLease( tunnel.Destination, tunnel.GatewayTunnelId );
        }

        private void UpdateNetworkWithPublishedLeases()
        {
            if ( SignedLeases is null )
            {
                Logging.LogDebug( "UpdateNetworkWithPublishedLeases: SignedLeases is null" );
                return;
            }

            if ( PublishDestination )
            {
                NetDb.Inst.FloodfillUpdate.TrigUpdateLeaseSet( SignedLeases );
            }

            var lsl = MyRemoteDestinations.DestinationsToUpdate;
            foreach( var dest in lsl )
            {
                UpdateRemoteDestinationWithLeases( dest.Value );
            }

            UpdateClientState();
        }

        private void UpdateRemoteDestinationWithLeases( DestLeaseInfo lsinfo )
        {
            var dtn = lsinfo.LastUse.DeltaToNow;

            if ( dtn > InitiatorInactivityLimit )
            {
                Logging.LogDebug( $"{this}: Inactivity to {lsinfo.LeaseSet.Destination.IdentHash.Id32Short} " +
                    $"{dtn} Stopping lease updates." );

                if ( dtn > InitiatorInactivityLimit * 2 + Tunnel.TunnelLifetime )
                {
                    Logging.LogDebug( $"{this}: Inactivity to {lsinfo.LeaseSet.Destination.IdentHash.Id32Short} " +
                        $"{dtn} Forgetting destination." );

                    MyRemoteDestinations.Remove( lsinfo?.LeaseSet?.Destination.IdentHash );
                }
                return;
            }

            if ( lsinfo?.LeaseSet is null || lsinfo?.LeaseSet?.Leases?.Count() == 0 )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No leases available." );

                MyRemoteDestinations.Remove( lsinfo?.LeaseSet?.Destination.IdentHash );
                return;
            }

            var outtunnel = SelectOutboundTunnel();

            if ( outtunnel is null )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No outbound tunnels available." );
                return;
            }

            var intunnel = SelectLease( lsinfo.LeaseSet );

            if ( intunnel is null )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No remote leases available." );
                return;
            }

            var lsupdate = new DatabaseStoreMessage( SignedLeases );

#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: Sending LS: {lsupdate}" );
#endif

            var sk = SessionKeys.GetOrAdd(
                        lsinfo.LeaseSet.Destination.IdentHash,
                        ( d ) => new SessionKeyOrigin( 
                                    this, 
                                    Destination,
                                    lsinfo.LeaseSet.Destination ) );

            sk.Send(
                    outtunnel,
                    intunnel,
                    SignedLeases,
                    SelectInboundTunnel,
                    new GarlicClove(
                        new GarlicCloveDeliveryDestination(
                            lsupdate,
                            lsinfo.LeaseSet.Destination.IdentHash ) ) );

            Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: " +
                $"Sending LS Garlic to {lsinfo.LeaseSet.Destination} from tunnel {outtunnel} to {intunnel}" );

            return;
        }

        public void LookupDestination( 
                I2PIdentHash dest, 
                DestinationLookupResult cb,
                object tag = null )
        {
            if ( cb is null ) return;

            var lls = MyRemoteDestinations.GetLeases( dest, true );
            if ( lls != null && IsLeasesGood( lls.LeaseSet ) )
            {
                cb?.Invoke( dest, lls.LeaseSet, tag );
                return;
            }

            var ls = NetDb.Inst.FindLeaseSet( dest );

            if ( IsLeasesGood( ls ) )
            {
                cb?.Invoke( dest, ls, tag );
                return;
            }

            Router.StartDestLookup( dest, cb, tag );
        }

        private static bool IsLeasesGood( I2PLeaseSet ls )
        {
            if ( ls != null )
            {
                if ( ls.Leases?.Any() ?? false )
                {
                    var max = ls.Leases.Max( l => l.EndDate );
                    if ( ( (DateTime)max - DateTime.UtcNow ).TotalMinutes > 3 )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected void HandleDestinationLookupResult( 
                I2PIdentHash hash, 
                I2PLeaseSet ls,
                object tag )
        {
            if ( ls is null )
            {
                if ( MyRemoteDestinations.LookupFailures( hash ) < 5 )
                {
                    Logging.LogDebug(
                        $"{this}: Lease set lookup failed. Trying again." );

                    LookupDestination( hash, HandleDestinationLookupResult, null );
                    return;
                }
                else
                {
                    Logging.LogDebug(
                        $"{this}: Lease set lookup failed. Giving up." );

                    MyRemoteDestinations.Remove( hash );
                }
                return;
            }

            Logging.LogDebug(
                    $"{this}: Lease set for {hash.Id32Short} found." );

            NetDb.Inst.AddLeaseSet( ls );
            MyRemoteDestinations.LeaseSetReceived( ls );
        }

        protected void UpdateClientState()
        {
            if ( InboundEstablishedPool.IsEmpty || OutboundEstablishedPool.IsEmpty )
            {
                ClientState = ClientStates.NoTunnels;
                return;
            }

            ClientState = ClientStates.Established;
        }

        public ClientStates Send( I2PDestination dest, byte[] buf )
        {
            return Send( dest, new BufLen( buf ) );
        }

        class UnsentDest
        {
            public I2PDestination Destination;
            public List<BufLen> Data;
        }

        TimeWindowDictionary<I2PIdentHash, UnsentDest> UnsentMessages =
                new TimeWindowDictionary<I2PIdentHash, UnsentDest>( TickSpan.Minutes( 2 ) );

        void UnsentMessagePush( I2PDestination dest, BufLen buf )
        {
            if ( UnsentMessages.TryGetValue( dest.IdentHash, out var msgs))
            {
                lock( msgs )
                {
                    msgs.Data.Add( buf );
                }
            }
            else
            {
                UnsentMessages[dest.IdentHash] = new UnsentDest
                {
                    Destination = dest,
                    Data = new List<BufLen>() { buf }
                };
            }
        }

        UnsentDest UnsentMessagePop( I2PIdentHash dest )
        {
            if ( UnsentMessages.TryRemove( dest, out var msgs ) )
            {
                return msgs;
            }

            return null;
        }

        ClientStates CheckSendPreconditions( 
                I2PIdentHash dest, 
                out OutboundTunnel outtunnel, 
                out I2PLease remotelease )
        {
            if ( InboundEstablishedPool.IsEmpty )
            {
                remotelease = null;
                outtunnel = null;
                return ClientStates.NoTunnels;
            }

            var rl = MyRemoteDestinations.GetLeases( dest, false );

            if ( rl is null )
            {
                remotelease = null;
                outtunnel = null;
                return ClientStates.NoLeases;
            }

            outtunnel = SelectOutboundTunnel();

            if ( outtunnel is null )
            {
                remotelease = null;
                return ClientStates.NoTunnels;
            }

            remotelease = SelectLease( rl.LeaseSet );

            if ( remotelease is null )
            {
                return ClientStates.NoLeases;
            }

            return ClientStates.Established;
        }

        public ClientStates Send( I2PDestination dest, BufLen buf )
        {
            if ( Terminated ) throw new InvalidOperationException( $"Destination {this} is terminated." );

            var result = CheckSendPreconditions( dest.IdentHash, out var outtunnel, out var remotelease );

            if ( result != ClientStates.Established )
            {
                switch ( result )
                {
                    case ClientStates.NoTunnels:
                        Logging.LogDebug( $"{this}: No inbound tunnels available." );
                        break;

                    case ClientStates.NoLeases:
                        Logging.LogDebug( $"{this}: No leases available." );
                        LookupDestination( dest.IdentHash, HandleDestinationLookupResult, null );
                        break;
                }
                UnsentMessagePush( dest, buf );
                return result;
            }

            var clove = new GarlicClove(
                 new GarlicCloveDeliveryDestination(
                     new DataMessage( buf ),
                     dest.IdentHash ) );

            var sk = SessionKeys.GetOrAdd(
                        dest.IdentHash,
                        ( d ) => new SessionKeyOrigin(
                                this, 
                                Destination, 
                                dest ) );

            return sk.Send(
                    outtunnel,
                    remotelease,
                    SignedLeases,
                    SelectInboundTunnel,
                    clove );
        }

        void SendUnsentMessages( I2PIdentHash dest )
        {
            if ( UnsentMessages.IsEmpty ) return;

            if ( CheckSendPreconditions( dest, out _, out _ ) 
                != ClientStates.Established ) return;

            var msgs = UnsentMessagePop( dest );
            if ( msgs is null ) return;

            foreach( var msg in msgs.Data )
            {
                ThreadPool.QueueUserWorkItem( a => Send( msgs.Destination, msg ) );
            }
        }

        void NetDb_LeaseSetUpdates( I2PLeaseSet ls )
        {
            SendUnsentMessages( ls.Destination.IdentHash );
        }

        private void IdentHashLookup_LeaseSetReceived( I2PLeaseSet ls )
        {
            ThreadPool.QueueUserWorkItem( a =>
                MyRemoteDestinations.LeaseSetReceived( ls ) );
            SendUnsentMessages( ls.Destination.IdentHash );
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Name} {Destination.IdentHash.Id32Short}";
        }
    }
}
