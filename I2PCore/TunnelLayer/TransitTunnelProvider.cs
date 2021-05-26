using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TransportLayer;
using I2PCore.Data;
using CM = System.Configuration.ConfigurationManager;
using System.Collections.Concurrent;
using static I2PCore.Utils.BufUtils;

namespace I2PCore.TunnelLayer
{
    internal class TransitTunnelProvider: ITunnelOwner
    {
        static readonly TickSpan BlockRecentTunnelsWindow = Tunnel.TunnelLifetime * 3;
        public const int BlockRecentTunnelsCount = 2;

        int MaxRunningTransitTunnels = 300;

        TunnelProvider TunnelMgr;

        ConcurrentDictionary<Tunnel, byte> RunningGatewayTunnels =
                new ConcurrentDictionary<Tunnel, byte>();

        ConcurrentDictionary<Tunnel, byte> RunningEndpointTunnels =
                new ConcurrentDictionary<Tunnel, byte>();

        ConcurrentDictionary<Tunnel, byte> RunningTransitTunnels =
                new ConcurrentDictionary<Tunnel, byte>();

        ItemFilterWindow<uint> AcceptedTunnelHashes = 
            new ItemFilterWindow<uint>( 
                    BlockRecentTunnelsWindow,
                    BlockRecentTunnelsCount );

        readonly ItemFilterWindow<I2PIdentHash> NextHopFilter = new ItemFilterWindow<I2PIdentHash>( TickSpan.Minutes( 5 ), 2 );

        internal TransitTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
            ReadAppConfig();

            tp.TunnelBuildRequestEvents += HandleTunnelBuildRecords;
        }

        readonly PeriodicAction Maintenance = new PeriodicAction( TickSpan.Minutes( 15 ) );

#if DEBUG
        PeriodicAction LogStatus = new PeriodicAction( TickSpan.Seconds( 30 ) );
#else
        PeriodicAction LogStatus = new PeriodicAction( TickSpan.Minutes( 2 ) );
#endif
        public void Execute()
        {
            LogStatus.Do( LogStatusReport );
        }

        private void LogStatusReport()
        {
            var gtc = RunningGatewayTunnels.Count;
            var gbrr = RunningGatewayTunnels.Sum( gt => 
                gt.Key.Bandwidth.ReceiveBandwidth.Bitrate ) / 1024f;
            var gbrs = RunningGatewayTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.Bitrate ) / 1024f;

            var gbr = RunningGatewayTunnels.Sum( gt =>
                gt.Key.Bandwidth.ReceiveBandwidth.DataBytes );
            var gbs = RunningGatewayTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.DataBytes );

            var etc = RunningEndpointTunnels.Count;
            var ebrr = RunningEndpointTunnels.Sum( gt =>
                gt.Key.Bandwidth.ReceiveBandwidth.Bitrate ) / 1024f;
            var ebrs = RunningEndpointTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.Bitrate ) / 1024f;

            var ebr = RunningEndpointTunnels.Sum( gt =>
                gt.Key.Bandwidth.ReceiveBandwidth.DataBytes );
            var ebs = RunningEndpointTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.DataBytes );

            var ttc = RunningTransitTunnels.Count;
            var tbrr = RunningTransitTunnels.Sum( gt =>
                gt.Key.Bandwidth.ReceiveBandwidth.Bitrate ) / 1024f;
            var tbrs = RunningTransitTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.Bitrate ) / 1024f;

            var tbr = RunningTransitTunnels.Sum( gt =>
                gt.Key.Bandwidth.ReceiveBandwidth.DataBytes );
            var tbs = RunningTransitTunnels.Sum( gt =>
                gt.Key.Bandwidth.SendBandwidth.DataBytes );

            Logging.LogInformation(
                $"Established gateway tunnels   : {gtc,2}, Send / Receive: " +
                $"{gbrs,8:F1} kbps / {gbrr,8:F1} kbps   " +
                $"{BytesToReadable( gbs ),10} / {BytesToReadable( gbr ),10}" );

            Logging.LogInformation(
                $"Established endpoint tunnels  : {etc,2}, Send / Receive: " +
                $"{ebrs,8:F1} kbps / {ebrr,8:F1} kbps   " +
                $"{BytesToReadable( ebs ),10} / {BytesToReadable( ebr ),10}" );

            Logging.LogInformation(
                $"Established transit tunnels   : {ttc,2}, Send / Receive: " +
                $"{tbrs,8:F1} kbps / {tbrr,8:F1} kbps   " +
                $"{BytesToReadable( tbs ),10} / {BytesToReadable( tbr ),10}" );
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["MaxTransitTunnels"] ) )
            {
                MaxRunningTransitTunnels = int.Parse( CM.AppSettings["MaxTransitTunnels"] );
            }
        }

        internal void HandleTunnelBuildRecords(
            II2NPHeader msg,
            TunnelBuildRequestDecrypt decrypt )
        {
            if ( decrypt.Decrypted.ToAnyone )
            {
                // Im outbound endpoint
                Logging.LogDebug( $"HandleTunnelBuildRecords: Outbound endpoint request {decrypt}" );
                HandleEndpointTunnelRequest( msg, decrypt );
                return;
            }

            if ( decrypt.Decrypted.FromAnyone )
            {
                // Im inbound gateway
                Logging.LogDebug( $"HandleTunnelBuildRecords: Inbound gateway request {decrypt}" );
                HandleGatewayTunnelRequest( msg, decrypt );
                return;
            }

            if ( decrypt.Decrypted.NextIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                // Im transit tunnel
                Logging.LogDebug( $"HandleTunnelBuildRecords: Transit tunnel request {decrypt}" );
                HandleTransitTunnelRequest( msg, decrypt );
                return;
            }

            throw new NotSupportedException();
        }

        private void HandleGatewayTunnelRequest(
            II2NPHeader msg,
            TunnelBuildRequestDecrypt decrypt )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.External,
                new TunnelInfo( new List<HopInfo>
                    {
                        new HopInfo(
                            RouterContext.Inst.MyRouterIdentity,
                            new I2PTunnelId() )
                    }
                ) );

            var tunnel = new GatewayTunnel( this, config, decrypt.Decrypted );
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( decrypt.Decrypted );

            var response = doaccept 
                    ? BuildResponseRecord.RequestResponse.Accept 
                    : BuildResponseRecord.DefaultErrorReply;

            Logging.LogDebug( $"HandleEndpointTunnelRequest {tunnel.TunnelDebugTrace}: " +
                $"{tunnel.Destination.Id32Short} Gateway tunnel request: {response} " +
                $"for tunnel id {tunnel.ReceiveTunnelId}." );

            var replymsg = CreateReplyMessage( msg, decrypt, response );

            if ( response == BuildResponseRecord.RequestResponse.Accept )
            {
                RunningGatewayTunnels[tunnel] = 1;
                TunnelMgr.AddTunnel( tunnel );
                AcceptedTunnelBuildRequest( decrypt.Decrypted );
            }

            TransportProvider.Send( tunnel.Destination, replymsg );
        }

        private void HandleEndpointTunnelRequest(
            II2NPHeader msg,
            TunnelBuildRequestDecrypt decrypt )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.External,
                new TunnelInfo( new List<HopInfo>
                    {
                        new HopInfo(
                            RouterContext.Inst.MyRouterIdentity,
                            new I2PTunnelId() )
                    }
                ) );

            var tunnel = new EndpointTunnel( this, config, decrypt.Decrypted );
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( decrypt.Decrypted );

            var response = doaccept 
                    ? BuildResponseRecord.RequestResponse.Accept 
                    : BuildResponseRecord.DefaultErrorReply;

            Logging.LogDebug( $"HandleEndpointTunnelRequest {tunnel.TunnelDebugTrace}: " +
                $"{tunnel.Destination.Id32Short} Endpoint tunnel request: {response} " +
                $"for tunnel id {tunnel.ReceiveTunnelId}." );

            var newrecords = decrypt.CreateTunnelBuildReplyRecords( response );

            var responsemessage = new VariableTunnelBuildReplyMessage(
                    newrecords.Select( r => new BuildResponseRecord( r ) ),
                    tunnel.ResponseMessageId );

            var buildreplymsg = new TunnelGatewayMessage( 
                    responsemessage,
                    tunnel.ResponseTunnelId );

            if ( response == BuildResponseRecord.RequestResponse.Accept )
            {
                RunningEndpointTunnels[tunnel] = 1;
                TunnelMgr.AddTunnel( tunnel );
                AcceptedTunnelBuildRequest( decrypt.Decrypted );
            }
            TransportProvider.Send( tunnel.Destination, buildreplymsg );
        }

        private void HandleTransitTunnelRequest(
            II2NPHeader msg,
            TunnelBuildRequestDecrypt decrypt )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.External,
                new TunnelInfo( new List<HopInfo>
                    {
                        new HopInfo(
                            RouterContext.Inst.MyRouterIdentity,
                            new I2PTunnelId() )
                    }
                ) );

            var tunnel = new TransitTunnel( this, config, decrypt.Decrypted );
            tunnel.EstablishedTime.SetNow();

            var doaccept = AcceptingTunnels( decrypt.Decrypted );

            var response = doaccept 
                    ? BuildResponseRecord.RequestResponse.Accept 
                    : BuildResponseRecord.DefaultErrorReply;

            Logging.LogDebug( $"HandleEndpointTunnelRequest {tunnel.TunnelDebugTrace}: " +
                $"{tunnel.Destination.Id32Short} Transit tunnel request: {response} " +
                $"for tunnel id {tunnel.ReceiveTunnelId}." );

            var replymsg = CreateReplyMessage( msg, decrypt, response );

            if ( response == BuildResponseRecord.RequestResponse.Accept )
            {
                RunningTransitTunnels[tunnel] = 1;
                TunnelMgr.AddTunnel( tunnel ); 
                AcceptedTunnelBuildRequest( decrypt.Decrypted );
            }
            TransportProvider.Send( tunnel.Destination, replymsg );
        }

        #region Request filter

        private bool AcceptingTunnels( BuildRequestRecord drec )
        {
            // TODO: Implement
            /*
            if ( !NextHopFilter.Update( drec.NextIdent ) )
            {
                Logging.LogDebug( () => string.Format( "TransitProvider AcceptingTunnels: Reject due same next destination. " +
                    "Running tunnels: {0}. Accept: {1}.",
                    RunningTunnels.Count, false ) );
            }
            */
            var currenttunnelcount = RunningGatewayTunnels.Count
                    + RunningEndpointTunnels.Count
                    + RunningTransitTunnels.Count;

            bool recent = HaveSeenTunnelBuildRequest( drec );
            if ( recent )
            {
                Logging.LogDebug( $"TransitProvider AcceptingTunnels: Reject due to similarity to recent tunnel. " +
                    $"Running tunnels: {currenttunnelcount}. Accept: false." );
                return false;
            }

            var result = currenttunnelcount < MaxRunningTransitTunnels;
            result &= TunnelMgr.AcceptTransitTunnels;

            if ( result )
            {
                AcceptedTunnelBuildRequest( drec );
            }

            Logging.LogDebug( $"TransitProvider AcceptingTunnels: Running tunnels: {currenttunnelcount}. Accept: {result}." );

            return result;
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
        public void TunnelEstablished( Tunnel tunnel )
        {
        }

        public void TunnelBuildTimeout( Tunnel tunnel )
        {
        }

        public void TunnelExpired( Tunnel tunnel )
        {
            Logging.LogDebug( $"TransitProvider: TunnelTimeout: {tunnel}" );
            RunningGatewayTunnels.TryRemove( tunnel, out _ );
            RunningEndpointTunnels.TryRemove( tunnel, out _ );
            RunningTransitTunnels.TryRemove( tunnel, out _ );
        }

        public void TunnelFailed( Tunnel tunnel )
        {
            TunnelExpired( tunnel );
        }

        #endregion

        private static I2NPMessage CreateReplyMessage(
                II2NPHeader msg,
                TunnelBuildRequestDecrypt decrypt,
                BuildResponseRecord.RequestResponse response )
        {
            var newrecords = decrypt.CreateTunnelBuildReplyRecords( response );

            if ( msg.MessageType == I2NPMessage.MessageTypes.VariableTunnelBuild )
            {
                return new VariableTunnelBuildMessage( newrecords );
            }
            else
            {
                return new TunnelBuildMessage( newrecords );
            }
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
