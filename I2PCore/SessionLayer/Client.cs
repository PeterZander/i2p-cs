using System;
using System.Collections.Concurrent;
using System.Linq;
using I2PCore.Data;
using I2PCore.TransportLayer;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public abstract class Client: IClient
    {
        public enum ClientStates { NoTunnels, NoLeases, Established }

        public event Action<ClientStates> ClientStateChanged;

        /// <summary>
        /// ls is null if lookup failed.
        /// </summary>
        public delegate void DestinationLookupResult( I2PIdentHash hash, I2PLeaseSet ls );

        public int InboundTunnelHopCount { get; protected set; } = 3;
        public int OutboundTunnelHopCount { get; protected set; } = 3;

        public int TargetInboundTunnelCount { get; protected set; } = 2;
        public int TargetOutboundTunnelCount { get; protected set; } = 2;

        /// <summary>
        /// Currently established Leases.
        /// </summary>
        /// <value>The leases.</value>
        public I2PLeaseSet Leases { get; private set; }

        /// <summary>
        /// I2P Destination address for this instance.
        /// </summary>
        /// <value>The destination.</value>
        public I2PDestination Destination { get; private set; }

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

        private ReceivedSessions IncommingSessions;
        protected readonly RemoteDestinations MyRemoteDestinations =
                new RemoteDestinations();

        protected I2PDestinationInfo ThisDestinationInfo { get; private set; }

        readonly ConcurrentDictionary<I2PIdentHash, DestinationLookupResult> UnresolvedDestinations =
                new ConcurrentDictionary<I2PIdentHash, DestinationLookupResult>();

        protected Client( I2PDestinationInfo destinfo )
        {
            ThisDestinationInfo = destinfo;
            Destination = new I2PDestination( ThisDestinationInfo );

            NetDb.Inst.IdentHashLookup.LeaseSetReceived += IdentHashLookup_LeaseSetReceived;
            NetDb.Inst.IdentHashLookup.LookupFailure += IdentHashLookup_LookupFailure;

            IncommingSessions = new ReceivedSessions( this, ThisDestinationInfo.PrivateKey );
            Leases = new I2PLeaseSet( Destination, null, new I2PLeaseInfo( ThisDestinationInfo ) );
        }

        protected virtual void NewIdentity()
        {
            ThisDestinationInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            Destination = new I2PDestination( ThisDestinationInfo );

            Leases = new I2PLeaseSet( Destination, null, new I2PLeaseInfo( ThisDestinationInfo ) );

            IncommingSessions = new ReceivedSessions( this, ThisDestinationInfo.PrivateKey );
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
                ClientStateChanged?.Invoke( ClientStateField );
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
                AddTunnelToLeaseSet( it );
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
        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 8 ) );

        public virtual void Execute()
        {
            KeepRemotesUpdated.Do( () =>
            {
                if ( MyRemoteDestinations.LastUpdate.DeltaToNow > TickSpan.Seconds( 30 ) )
                {
                    Logging.LogDebug(
                        $"{this}: MyRemoteDestinations idle update." );

                    UpdateRemoteDestinationWithLeases();
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
                RemoveTunnelFromLeaseSet( (InboundTunnel)tunnel );
            }
        }

        void InboundTunnel_GarlicMessageReceived( GarlicMessage msg )
        {
            try
            {
                var decr = IncommingSessions.DecryptMessage( msg.Garlic );
                if ( decr == null ) return;

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

                                    if ( dbsmsg.LeaseSet != null )
                                    {
                                        Logging.LogDebug( $"{this}: New lease set received in stream for {dbsmsg.LeaseSet.Destination}." );
                                        NetDb.Inst.AddLeaseSet( dbsmsg.LeaseSet );
                                        MyRemoteDestinations.LeaseSetReceived( dbsmsg.LeaseSet );
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

        internal abstract void DestinationMessageReceived( I2NPMessage message );

        internal virtual void AddTunnelToLeaseSet( InboundTunnel tunnel )
        {
            Leases.AddLease(
                new I2PLease(
                    tunnel.Destination,
                    tunnel.GatewayTunnelId ) );

            LeaseSetUpdatedPrivate( true );
        }

        internal virtual void RemoveTunnelFromLeaseSet( InboundTunnel tunnel )
        {
            Leases.RemoveLease( tunnel.Destination, tunnel.GatewayTunnelId );
            LeaseSetUpdatedPrivate( false );
        }

        protected abstract void LeaseSetUpdated( bool newlease );
        private void LeaseSetUpdatedPrivate( bool newlease )
        {
            if ( newlease )
            {
                UpdateRemoteDestinationWithLeases();
            }
            LeaseSetUpdated( newlease );
        }

        private void UpdateRemoteDestinationWithLeases()
        {
            var lsl = MyRemoteDestinations.DestinationsToUpdate;

            foreach( var dest in lsl )
            {
                UpdateRemoteDestinationWithLeases( dest.Value );
            }

            UpdateClientState();
        }

        private void UpdateRemoteDestinationWithLeases( I2PLeaseSet ls )
        {
            if ( ls is null )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No leases available." );
                return;
            }

            var outtunnel = SelectOutboundTunnel();

            if ( outtunnel is null )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No outbound tunnels available." );
                return;
            }

            var intunnel = SelectLease( ls );

            if ( intunnel is null )
            {
                Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: No remote leases available." );
                return;
            }

            var lsupdate = new DatabaseStoreMessage( Leases );

#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases: Sending LS: {lsupdate}" );
#endif

            var garlic = new Garlic(
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        lsupdate,
                        ls.Destination.IdentHash ) ) );

            var egmsg = Garlic.EGEncryptGarlic(
                    garlic,
                    ls.Destination.PublicKey,
                    new I2PSessionKey(),
                    null );

            Logging.LogDebug( $"{this} UpdateRemoteDestinationWithLeases:Sending LS Garlic from tunnel {outtunnel} to {intunnel}" );

            outtunnel.Send(
                new TunnelMessageTunnel(
                    new GarlicMessage( egmsg ),
                    intunnel.TunnelGw, intunnel.TunnelId ) );

            return;
        }

        public void LookupDestination( I2PIdentHash hash, DestinationLookupResult cb )
        {
            if ( cb == null ) return;

            StartDestLookup( hash, cb );
        }

        void IdentHashLookup_LookupFailure( I2PIdentHash key )
        {
            if ( UnresolvedDestinations.TryRemove( key, out var cb ) )
            {
                cb( key, null );
            }
        }

        void IdentHashLookup_LeaseSetReceived( I2PLeaseSet ls )
        {
            var key = ls.Destination.IdentHash;

            if ( UnresolvedDestinations.TryRemove( key, out var cb ) )
            {
                cb( key, ls );
            }
        }

        void StartDestLookup( I2PIdentHash hash, DestinationLookupResult cb )
        {
            UnresolvedDestinations[hash] = cb;

            NetDb.Inst.IdentHashLookup.LookupLeaseSet( hash );
        }

        protected void UpdateClientState()
        {
            if ( InboundEstablishedPool.IsEmpty || OutboundEstablishedPool.IsEmpty )
            {
                ClientState = ClientStates.NoTunnels;
                return;
            }

            if ( MyRemoteDestinations.IsEmpty )
            {
                ClientState = ClientStates.NoLeases;
                return;
            }

            ClientState = ClientStates.Established;
        }
    }
}
