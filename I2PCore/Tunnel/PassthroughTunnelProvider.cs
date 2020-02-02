using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Utils;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Transport;
using I2PCore.Data;

namespace I2PCore.Tunnel
{
    internal class PassthroughTunnelProvider
    {
        static readonly TickSpan BlockRecentTunnelsWindow = TickSpan.Seconds( Tunnel.TunnelLifetimeSeconds * 3 );
        const int MaxRunningPassthroughTunnels = 300;

        TunnelProvider TunnelMgr;

        List<Tunnel> RunningTunnels = new List<Tunnel>();

        ItemFilterWindow<uint> AcceptedTunnelHashes = new ItemFilterWindow<uint>( BlockRecentTunnelsWindow, 2 );
        ItemFilterWindow<I2PIdentHash> NextHopFilter = new ItemFilterWindow<I2PIdentHash>( TickSpan.Minutes( 5 ), 2 );

        internal PassthroughTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
        }

        PeriodicAction Maintenance = new PeriodicAction( TickSpan.Minutes( 15 ) );

        internal void Execute()
        {
        }

        internal void HandleTunnelBuildRecords(
            II2NPHeader msg,
            IEnumerable<AesEGBuildRequestRecord> records,
            EGBuildRequestRecord myrec,
            BuildRequestRecord drec )
        {
            if ( drec.ToAnyone )
            {
                // Im outbound endpoint
                Logging.LogDebug( "HandleTunnelBuildRecords: Outbound endpoint request " + drec.ToString() );
                HandleEndpointTunnelRequest( msg, records, myrec, drec );
                return;
            }

            if ( drec.FromAnyone )
            {
                // Im inbound gateway
                Logging.LogDebug( "HandleTunnelBuildRecords: Inbound gateway request " + drec.ToString() );
                HandleGatewayTunnelRequest( msg, records, myrec, drec );
                return;
            }

            if ( drec.NextIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                // Im passthrough tunnel
                Logging.LogDebug( "HandleTunnelBuildRecords: Passthrough tunnel request " + drec.ToString() );
                HandlePassthroughTunnelRequest( msg, records, myrec, drec );
                return;
            }

            throw new NotSupportedException();
        }

        private void HandleGatewayTunnelRequest(
            II2NPHeader msg,
            IEnumerable<AesEGBuildRequestRecord> records,
            EGBuildRequestRecord myrec,
            BuildRequestRecord drec )
        {
            var tunnel = new GatewayTunnel( drec );
            var replykey = drec.ReplyKey.Key.Clone();
            var replyiv = drec.ReplyIV.Clone();
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( drec );

            var response = doaccept ? BuildResponseRecord.RequestResponse.Accept : BuildResponseRecord.DefaultErrorReply;
            Logging.LogDebug( () => string.Format( "HandleGatewayTunnelRequest {3}: {0} Gateway tunnel request: {1} for tunnel id {2}.",
                tunnel.Destination.Id32Short,
                response, 
                tunnel.ReceiveTunnelId, 
                tunnel.TunnelDebugTrace ) );
            TunnelProvider.UpdateTunnelBuildReply( records, myrec, replykey, replyiv, response );

            if ( response == BuildResponseRecord.RequestResponse.Accept ) 
            {
                AddTunnel( tunnel );
                TunnelMgr.AddExternalTunnel( tunnel );
                AcceptedTunnelBuildRequest( drec );
            }
            TransportProvider.Send( tunnel.Destination, msg.Message );
        }

        private void HandleEndpointTunnelRequest(
            II2NPHeader msg,
            IEnumerable<AesEGBuildRequestRecord> records,
            EGBuildRequestRecord myrec,
            BuildRequestRecord drec )
        {
            var tunnel = new EndpointTunnel( drec );
            var replykey = drec.ReplyKey.Key.Clone();
            var replyiv = drec.ReplyIV.Clone();
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( drec );

            var response = doaccept ? BuildResponseRecord.RequestResponse.Accept : BuildResponseRecord.DefaultErrorReply;
            Logging.LogDebug( () => string.Format( "HandleEndpointTunnelRequest {3}: {0} Endpoint tunnel request: {1} for tunnel id {2}.",
                tunnel.Destination.Id32Short,
                response, 
                tunnel.ReceiveTunnelId, 
                tunnel.TunnelDebugTrace ) );
            TunnelProvider.UpdateTunnelBuildReply( records, myrec, replykey, replyiv, response );

            var responsemessage = new VariableTunnelBuildReplyMessage( records.Select( r => new BuildResponseRecord( r ) ) );
            var buildreplymsg = new TunnelGatewayMessage( responsemessage.GetHeader16( tunnel.ResponseMessageId ), tunnel.ResponseTunnelId );

            if ( response == BuildResponseRecord.RequestResponse.Accept )
            {
                AddTunnel( tunnel );
                TunnelMgr.AddExternalTunnel( tunnel );
                AcceptedTunnelBuildRequest( drec );
            }
            TransportProvider.Send( tunnel.Destination, buildreplymsg );
        }

        private void HandlePassthroughTunnelRequest(
            II2NPHeader msg,
            IEnumerable<AesEGBuildRequestRecord> records,
            EGBuildRequestRecord myrec,
            BuildRequestRecord drec )
        {
            var tunnel = new PassthroughTunnel( drec );
            var replykey = drec.ReplyKey.Key.Clone();
            var replyiv = drec.ReplyIV.Clone();
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( drec );

            var response = doaccept ? BuildResponseRecord.RequestResponse.Accept : BuildResponseRecord.DefaultErrorReply;
            Logging.LogDebug( () => string.Format( "HandlePassthroughTunnelRequest {3}: {0} Passthrough tunnel request: {1} for tunnel id {2}.",
                tunnel.Destination.Id32Short,
                response, 
                tunnel.ReceiveTunnelId, 
                tunnel.TunnelDebugTrace ) );
            TunnelProvider.UpdateTunnelBuildReply( records, myrec, replykey, replyiv, response );

            if ( response == BuildResponseRecord.RequestResponse.Accept )
            {
                AddTunnel( tunnel );
                TunnelMgr.AddExternalTunnel( tunnel ); 
                AcceptedTunnelBuildRequest( drec );
            }
            TransportProvider.Send( tunnel.Destination, msg.Message );
        }

        private void AddTunnel( Tunnel tunnel )
        {
            lock ( RunningTunnels )
            {
                RunningTunnels.Add( tunnel );
            }
        }

        private void RemoveTunnel( Tunnel tunnel )
        {
            lock ( RunningTunnels )
            {
            again:
                var match = RunningTunnels.IndexOf( tunnel );
                if ( match != -1 )
                {
                    RunningTunnels.RemoveAt( match );
                    goto again;
                }
            }
        }

        #region Request filter

        private bool AcceptingTunnels( BuildRequestRecord drec )
        {
            // TODO: Implement
            /*
            if ( !NextHopFilter.Update( drec.NextIdent ) )
            {
                Logging.LogDebug( () => string.Format( "PassthroughProvider AcceptingTunnels: Reject due same next destination. " +
                    "Running tunnels: {0}. Accept: {1}.",
                    RunningTunnels.Count, false ) );
            }
            */
            bool recent = HaveSeenTunnelBuildRequest( drec );
            if ( recent )
            {
                Logging.LogDebug( () => string.Format( "PassthroughProvider AcceptingTunnels: Reject due to similarity to recent tunnel. " +
                    "Running tunnels: {0}. Accept: {1}.",
                    RunningTunnels.Count, false ) );
                return false;
            }

            var result = RunningTunnels.Count < MaxRunningPassthroughTunnels;
            result &= TunnelMgr.ClientsMgr.ClientTunnelsStatusOk;

            if ( result )
            {
                AcceptedTunnelBuildRequest( drec );
            }

            Logging.LogDebug( () => string.Format( "PassthroughProvider AcceptingTunnels: Running tunnels: {0}. Accept: {1}.",
                RunningTunnels.Count, result ) );

            return result;
            //if ( result ) result = NetDb.Inst.GetConnectionRank( id ) 
        }

        void AcceptedTunnelBuildRequest( BuildRequestRecord drec )
        {
        }

        void AcceptedTunnelBuildRequest( uint hash )
        {
        }

        bool HaveSeenTunnelBuildRequest( BuildRequestRecord drec )
        {
            return !AcceptedTunnelHashes.Update( drec.GetReducedHash() );
        }

        #endregion

        #region TunnelEvents
        internal void TunnelEstablished( Tunnel tunnel )
        {
            // Never called, I think
        }

        internal void TunnelBuildTimeout( Tunnel tunnel )
        {
            // Never called, I think
        }

        internal void TunnelTimeout( Tunnel tunnel )
        {
            Logging.LogDebug( "PassthroughProvider: TunnelTimeout: " + tunnel.ToString() );
            RemoveTunnel( tunnel );
        }
        #endregion
    }
}
