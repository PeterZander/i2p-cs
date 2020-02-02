using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Utils;
using I2PCore.Tunnel;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Transport;
using I2PCore.Data;

namespace I2PCore
{
    public class FloodfillUpdater
    {
        public readonly TickSpan DatabaseStoreNonReplyTimeout = TickSpan.Seconds( 8 );

        PeriodicAction StartNewUpdate = new PeriodicAction( TickSpan.Seconds( NetDb.RouterInfoExpiryTimeSeconds / 5 ), true );
        PeriodicAction CheckForTimouts = new PeriodicAction( TickSpan.Seconds( 5 ) );

        class FFUpdateRequestInfo
        {
            public readonly TickCounter Start = new TickCounter();
            public readonly I2PIdentHash FFRouter;

            public FFUpdateRequestInfo( I2PIdentHash id )
            {
                FFRouter = id;
            }
        }

        Dictionary<uint, FFUpdateRequestInfo> OutstandingRequests = new Dictionary<uint, FFUpdateRequestInfo>();

        public FloodfillUpdater()
        {
            InboundTunnel.DeliveryStatusReceived += new Action<DeliveryStatusMessage>( InboundTunnel_DeliveryStatusReceived );
            TunnelProvider.DeliveryStatusReceived += new Action<DeliveryStatusMessage>( InboundTunnel_DeliveryStatusReceived );
        }

        void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage msg )
        {
            lock ( OutstandingRequests )
            {
                if ( OutstandingRequests.TryGetValue( msg.MessageId, out var info ) )
                {
                    Logging.Log( string.Format( "FloodfillUpdater: Floodfill delivery status {0,10} from {1} received in {2} seconds.",
                        msg.MessageId, info.FFRouter.Id32Short, info.Start.DeltaToNowSeconds ) );

                    OutstandingRequests.Remove( msg.MessageId );

                    NetDb.Inst.Statistics.FloodfillUpdateSuccess( info.FFRouter );
                }
            }
        }

        public void Run()
        {
            StartNewUpdate.Do( StartNewUpdates );
            CheckForTimouts.Do( CheckTimeouts );
        }

        public void TrigUpdate()
        {
            if ( StartNewUpdate.LastAction.DeltaToNowSeconds > 15 )
            {
                StartNewUpdate.TimeToAction = TickSpan.Seconds( 5 );
            }
        }

        void StartNewUpdates()
        {
            var list = GetNewFFList();

            foreach ( var ff in list )
            {
                try
                {
                    var token = BufUtils.RandomUint() | 1;

                    Logging.Log( string.Format( "FloodfillUpdater: {0}, token: {1,10}, dist: {2}.",
                        ff.Id32Short, token,
                        ff ^ RouterContext.Inst.MyRouterIdentity.IdentHash.RoutingKey ) );

                    SendUpdate( ff, token );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        private void SendUpdate( I2PIdentHash ff, uint token )
        {
            // If greater than zero, a DeliveryStatusMessage
            // is requested with the Message ID set to the value of the Reply Token.
            // A floodfill router is also expected to flood the data to the closest floodfill peers
            // if the token is greater than zero.
            // https://geti2p.net/spec/i2np#databasestore

            var ds = new DatabaseStoreMessage( RouterContext.Inst.MyRouterInfo,
                token, RouterContext.Inst.MyRouterInfo.Identity.IdentHash, 0 );

            lock ( OutstandingRequests )
            {
                OutstandingRequests[token] = new FFUpdateRequestInfo( ff );
            }

            TransportProvider.Send( ff, ds );
        }

        private void SendUpdateTunnelReply( I2PIdentHash ff, uint token )
        {
            // If greater than zero, a DeliveryStatusMessage
            // is requested with the Message ID set to the value of the Reply Token.
            // A floodfill router is also expected to flood the data to the closest floodfill peers
            // if the token is greater than zero.
            // https://geti2p.net/spec/i2np#databasestore

            var replytunnel = TunnelProvider.Inst.GetInboundTunnel();
            var ds = new DatabaseStoreMessage( RouterContext.Inst.MyRouterInfo,
                token, replytunnel.Destination, replytunnel.ReceiveTunnelId );

            lock ( OutstandingRequests )
            {
                OutstandingRequests[token] = new FFUpdateRequestInfo( ff );
            }

            TransportProvider.Send( ff, ds );
        }

        void CheckTimeouts()
        {
            KeyValuePair<uint,FFUpdateRequestInfo>[] timeout;

            lock ( OutstandingRequests )
            {
                timeout = OutstandingRequests.Where( r => r.Value.Start.DeltaToNow > DatabaseStoreNonReplyTimeout ).ToArray();
                foreach ( var item in timeout.ToArray() ) OutstandingRequests.Remove( item.Key );
            }

            foreach ( var one in timeout )
            {
                Logging.Log( string.Format( "FloodfillUpdater: Update {0,10} to {1} failed with timeout.",
                    one.Key, one.Value.FFRouter.Id32Short ) );

                NetDb.Inst.Statistics.FloodfillUpdateTimeout( one.Value.FFRouter );
            }

            // Get a wider selection
            var list = NetDb.Inst.GetClosestFloodfill( RouterContext.Inst.MyRouterIdentity.IdentHash, 100, null, false );
            list = list.Shuffle().Take( timeout.Length );

            foreach ( var ff in list )
            {
                var token = BufUtils.RandomUint() | 1;

                Logging.Log( string.Format( "FloodfillUpdater: replacement update {0}, token {1,10}, dist: {2}.",
                    ff.Id32Short, token,
                    ff ^ RouterContext.Inst.MyRouterIdentity.IdentHash.RoutingKey ) );

                SendUpdate( ff, token );
            }
        }

        private static IEnumerable<I2PIdentHash> GetNewFFList()
        {
            var list = NetDb.Inst.GetClosestFloodfill( RouterContext.Inst.MyRouterIdentity.IdentHash, 20, null, false );
            if ( list == null || list.Count() == 0 ) list = NetDb.Inst.GetRandomFloodfillRouter( true, 20 );
            list = list.Shuffle().Take( 4 );

            if ( DateTime.UtcNow.Hour >= 23 )
            {
                var nextlist = NetDb.Inst.GetClosestFloodfill( RouterContext.Inst.MyRouterIdentity.IdentHash, 20, null, true ).Shuffle().Take( 4 );
                if ( nextlist != null ) list = list.Concat( nextlist );
            }
            return list;
        }
    }
}
