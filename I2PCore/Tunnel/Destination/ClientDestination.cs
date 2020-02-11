using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Utils;
using I2PCore.Transport;
using CM = System.Configuration.ConfigurationManager;

namespace I2PCore.Tunnel
{
    public class ClientDestination
    {
        public int InboundTunnelHopCount = 3;
        public int OutboundTunnelHopCount = 3;

        public int TargetInboundTunnelCount = 2;
        public int TargetOutboundTunnelCount = 2;

        ConcurrentDictionary<OutboundTunnel, byte> OutboundPending = new ConcurrentDictionary<OutboundTunnel, byte>();
        ConcurrentDictionary<InboundTunnel, byte> InboundPending = new ConcurrentDictionary<InboundTunnel, byte>();

        ConcurrentDictionary<OutboundTunnel, byte> OutboundEstablishedPool = new ConcurrentDictionary<OutboundTunnel, byte>();
        ConcurrentDictionary<InboundTunnel, byte> InboundEstablishedPool = new ConcurrentDictionary<InboundTunnel, byte>();

        internal int InboundTunnelsNeeded 
        { 
            get 
            {
                return TargetInboundTunnelCount 
                    - InboundEstablishedPool.Count( t => !t.Key.NeedsRecreation ) 
                    - InboundPending.Count;
            }
        }

        internal int OutboundTunnelsNeeded
        {
            get
            {
                return TargetOutboundTunnelCount 
                    - OutboundEstablishedPool.Count( t => !t.Key.NeedsRecreation ) 
                    - OutboundPending.Count;
            }
        }

        internal bool ClientTunnelsStatusOk
        {
            get
            {
                return InboundEstablishedPool.Count >= TargetInboundTunnelCount
                    && OutboundEstablishedPool.Count >= TargetOutboundTunnelCount;
            }
        }

        I2PDestinationInfo ThisDestination;
        I2PDestination MyDestination;
        readonly bool PublishDestination;

        I2PLeaseSet LeaseSet;
        ClientTunnelProvider ClientTunnelMgr;

        ReceivedSessions IncommingSessions;
        DestinationSessions Destinations;

        ClientDestination TestRemoteDest;

        public delegate void DestinationDataReceived( BufLen data );
        public event DestinationDataReceived DataReceived;

        /// <summary>
        /// Dest is null if lookup failed.
        /// </summary>
        public delegate void DestinationLookupResult( I2PIdentHash hash, I2PLeaseSet ls );

        internal ClientDestination( ClientTunnelProvider tp, I2PDestinationInfo dest, bool publishdest )
        {
            ClientTunnelMgr = tp;
            PublishDestination = publishdest;
            ThisDestination = dest;

            ReadAppConfig();

            MyDestination = new I2PDestination( ThisDestination );

            LeaseSet = new I2PLeaseSet( MyDestination, null, new I2PLeaseInfo( ThisDestination ) );

            IncommingSessions = new ReceivedSessions( ThisDestination.PrivateKey );
            Destinations = new DestinationSessions( ( ls, header, inf ) =>
            {
                Logging.LogDebug( string.Format( "ClientDestination: Execute: Sending data. TrackingId: {0} ({1}) ack {2}, msg {3}.",
                    inf.TrackingId, inf.KeyType, inf.AckMessageId, header ) );

                if ( OutboundEstablishedPool.Count == 0 ) throw new FailedToConnectException( "No outbound tunnels available" );
                if ( ls == null || ls.Leases.Count == 0 ) throw new FailedToConnectException( "No leases is available" );

                var outtunnel = OutboundEstablishedPool.Random();
                var lease = ls.Leases.Random();

                outtunnel.Key.Send(
                    new TunnelMessageTunnel( header, lease.TunnelGw, lease.TunnelId ) );
            }, () => InboundEstablishedPool.Random().Key );

            NetDb.Inst.IdentHashLookup.LeaseSetReceived += new IdentResolver.IdentResolverResultLeaseSet( IdentHashLookup_LeaseSetReceived );
            NetDb.Inst.IdentHashLookup.LookupFailure += new IdentResolver.IdentResolverResultFail( IdentHashLookup_LookupFailure );
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["InboundTunnelsPerDestination"] ) )
            {
                TargetInboundTunnelCount = int.Parse( CM.AppSettings["InboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["OutboundTunnelsPerDestination"] ) )
            {
                TargetOutboundTunnelCount = int.Parse( CM.AppSettings["OutboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["InboundTunnelHops"] ) )
            {
                InboundTunnelHopCount = int.Parse( CM.AppSettings["InboundTunnelHops"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["OutboundTunnelHops"] ) )
            {
                OutboundTunnelHopCount = int.Parse( CM.AppSettings["OutboundTunnelHops"] );
            }
        }

        public void Send( I2PLeaseSet ls, byte[] data, bool ack )
        {
            Destinations.RemoteLeaseSetUpdated( ls );
            Destinations.Send( ls.Destination, ack,
                new GarlicCloveDeliveryDestination( new DataMessage( new BufLen( data ) ), ls.Destination.IdentHash ) );
        }

        public void LookupDestination( I2PIdentHash hash, DestinationLookupResult cb )
        {
            if ( cb == null ) return;

            StartDestLookup( hash, cb );
        }

        // TODO: Remove
        internal ClientDestination( ClientTunnelProvider tp, bool publishdest, ClientDestination remotedest )
        {
            ClientTunnelMgr = tp;
            PublishDestination = publishdest;
            NewIdentity();

            TestRemoteDest = remotedest;
        }

        PeriodicAction SendNewData = new PeriodicAction( TickSpan.Seconds( 30 ) );
        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 8 ) );

        DataMessage BigMessage = new DataMessage( new BufLen( BufUtils.Random( 14 * 1024 ) ) );

        internal void Execute()
        {
            if ( TestRemoteDest != null && 
                OutboundEstablishedPool.Count >= 2 &&
                InboundEstablishedPool.Count >= 2 &&
                TestRemoteDest.InboundEstablishedPool.Count >= 2 ) 
            {
                try
                {
                    SendNewData.Do( () =>
                    {
                        var dest = TestRemoteDest.MyDestination;
                        var origmessage = new DeliveryStatusMessage( I2NPHeader.GenerateMessageId() );
                        var big = BufUtils.RandomInt( 100 ) < 5;

                        Destinations.Send( dest, true,
                            new GarlicCloveDeliveryDestination(
                                origmessage, dest.IdentHash ),
                            ( big ?
                                new GarlicCloveDeliveryDestination( BigMessage, dest.IdentHash ) :
                                new GarlicCloveDeliveryDestination( origmessage, dest.IdentHash ) ) );
                    } );
                }
                catch ( Exception ex )
                {
                    SendNewData.Reset();
                    Logging.Log( ex );
                }
            }

            QueueStatusLog.Do( () =>
            {
                Logging.LogInformation( string.Format(
                    "ClientTunnelProvider {4}: Established tunnels in: {0,2}, out: {1,2}. Pending in: {2,2}, out {3,2}",
                    InboundEstablishedPool.Count, OutboundEstablishedPool.Count,
                    InboundPending.Count, OutboundPending.Count, MyDestination.IdentHash.Id32Short ) );
            } );

            Destinations.Run();
        }

        private void NewIdentity()
        {
            ThisDestination = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            MyDestination = new I2PDestination( ThisDestination );

            LeaseSet = new I2PLeaseSet( MyDestination, null, new I2PLeaseInfo( ThisDestination ) );

            IncommingSessions = new ReceivedSessions( ThisDestination.PrivateKey );
            Destinations = new DestinationSessions( ( dest, header, inf ) =>
            {
                Logging.LogDebug( string.Format( "ClientDestination: Execute: Sending data. TrackingId: {0} ({1}) ack {2}, msg {3}.",
                    inf.TrackingId, inf.KeyType, inf.AckMessageId, header ) );

                var outtunnel = OutboundEstablishedPool.Random().Key;
                outtunnel.Send(
                    new TunnelMessageTunnel( header, TestRemoteDest.InboundEstablishedPool.Random().Key ) );
            },
            () => InboundEstablishedPool.Random().Key );
        }

        internal void TunnelEstablished( Tunnel tunnel )
        {
            RemovePendingTunnel( tunnel );

            if ( tunnel is OutboundTunnel ot )
            {
                OutboundEstablishedPool[ot] = 0;
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundEstablishedPool[it] = 0;
                it.GarlicMessageReceived += new Action<I2PCore.Tunnel.I2NP.Messages.GarlicMessage>( InboundTunnel_GarlicMessageReceived );
                AddTunnelToLeaseSet( it );
            }
        }

        void InboundTunnel_GarlicMessageReceived( GarlicMessage msg )
        {
            try
            {
                var decr = IncommingSessions.DecryptMessage( msg.Garlic );
                if ( decr == null ) return;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( "ClientDestination: GarlicMessageReceived: " + decr.ToString() );
#endif

                foreach ( var clove in decr.Cloves )
                {
                    try
                    {
                        switch ( clove.Delivery.Delivery )
                        {
                            case GarlicCloveDelivery.DeliveryMethod.Local:
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( () => string.Format(
                                    "ClientDestination: GarlicMessageReceived: Delivered Local: {0}", clove.Message ) );
#endif
                                TunnelProvider.Inst.DistributeIncomingMessage( null, clove.Message.Header16 );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Router:
                                var dest = ( (GarlicCloveDeliveryRouter)clove.Delivery ).Destination;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( () => string.Format(
                                    "ClientDestination: GarlicMessageReceived: Delivered Router: {0} {1}",
                                    dest.Id32Short, clove.Message ) );
#endif
                                TransportProvider.Send( dest, clove.Message );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Tunnel:
                                var tone = (GarlicCloveDeliveryTunnel)clove.Delivery;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( () => string.Format( 
                                    "ClientDestination: GarlicMessageReceived: Delivered Tunnel: {0} TunnelId: {1} {2}",
                                    tone.Destination.Id32Short, tone.Tunnel, clove.Message ) );
#endif
                                TransportProvider.Send( tone.Destination, new TunnelGatewayMessage( clove.Message.Header16, tone.Tunnel ) );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Destination:
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( () => string.Format(
                                    "ClientDestination: GarlicMessageReceived: Delivered Destination: {0}", clove.Message ) );
#endif
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

        void DestinationMessageReceived( I2NPMessage msg )
        {
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( "ClientDestination: DestinationMessageReceived: " + msg.ToString() );
#endif
            if ( msg.MessageType == I2NPMessage.MessageTypes.Data && DataReceived != null )
            {
                var dmsg = (DataMessage)msg.Header16.Message;
                DataReceived( dmsg.Payload );
            }

            if ( msg.MessageType == I2NPMessage.MessageTypes.DatabaseStore )
            {
                var dbsmsg = (DatabaseStoreMessage)msg.Header16.Message;
                if ( dbsmsg.LeaseSet != null ) Destinations.RemoteLeaseSetUpdated( dbsmsg.LeaseSet );
            }
        }

        #region Lease mgmt
        void AddTunnelToLeaseSet( InboundTunnel tunnel )
        {
            LeaseSet.AddLease( new I2PLease( tunnel.Destination, tunnel.GatewayTunnelId, new I2PDate( DateTime.UtcNow.AddMinutes( 10 ) ) ) );
            Destinations.LocalLeaseSetUpdated( LeaseSet );
            LeaseSetUpdated( PublishDestination );
        }

        void RemoveTunnelFromLeaseSet( InboundTunnel tunnel )
        {
            LeaseSet.RemoveLease( tunnel.Destination, tunnel.GatewayTunnelId );
            LeaseSetUpdated( false );
        }

        private void LeaseSetUpdated( bool broadcast )
        {
            if ( broadcast ) ClientTunnelMgr.TunnelMgr.LocalLeaseSetChanged( LeaseSet );
        }
        #endregion

        #region Tunnel list mgmt

        internal void AddOutboundPending( OutboundTunnel tunnel )
        {
            OutboundPending[tunnel] = 0;
        }

        internal void AddInboundPending( InboundTunnel tunnel )
        {
            InboundPending[tunnel] = 0;
        }

        internal void RemoveTunnel( Tunnel tunnel )
        {
            RemovePendingTunnel( tunnel );
            RemovePoolTunnel( tunnel );
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
        #endregion

        #region Destination lookup

        Dictionary<I2PIdentHash, DestinationLookupResult> UnresolvedDestinations = new Dictionary<I2PIdentHash, DestinationLookupResult>();

        void IdentHashLookup_LookupFailure( I2PIdentHash key )
        {
            lock ( UnresolvedDestinations )
            {
                if ( UnresolvedDestinations.TryGetValue( key, out var cb ) )
                {
                    cb( key, null );
                    UnresolvedDestinations.Remove( key );
                }
            }
        }

        void IdentHashLookup_LeaseSetReceived( I2PLeaseSet ls )
        {
            var key = ls.Destination.IdentHash;

            lock ( UnresolvedDestinations )
            {
                if ( UnresolvedDestinations.TryGetValue( key, out var cb ) )
                {
                    cb( key, ls );
                    UnresolvedDestinations.Remove( key );
                }
            }
        }

        void StartDestLookup( I2PIdentHash hash, DestinationLookupResult cb )
        {
            lock ( UnresolvedDestinations )
            {
                UnresolvedDestinations[hash] = cb;
            }
            NetDb.Inst.IdentHashLookup.LookupLeaseSet( hash );
        }

        #endregion
    }
}
