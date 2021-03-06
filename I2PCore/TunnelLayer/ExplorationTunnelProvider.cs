﻿using System.Linq;
using I2PCore.Utils;
using CM = System.Configuration.ConfigurationManager;
using System.Collections.Concurrent;

namespace I2PCore.TunnelLayer
{
    internal class ExplorationTunnelProvider: ITunnelOwner
    {
        public int TargetOutboundExploratoryTunnelCount = 20;
        public int TargetInboundExploratoryTunnelCount = 20;
        public int DefaultExploratoryTunnelHopCount = 1;
        public static readonly TickSpan TimeBetweenTunnelBuilds = TickSpan.Seconds( 2 );

        ConcurrentDictionary<Tunnel, bool> Tunnels = new ConcurrentDictionary<Tunnel, bool>();

        TunnelProvider TunnelMgr;
        public SuccessRatio ExplorationTunnelBuildSuccessRatio = new SuccessRatio();

        internal ExplorationTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
            ReadAppConfig();
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["OutboundExploratoryTunnels"] ) )
            {
                TargetOutboundExploratoryTunnelCount = int.Parse( CM.AppSettings["OutboundExploratoryTunnels"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["InboundExploratoryTunnels"] ) )
            {
                TargetInboundExploratoryTunnelCount = int.Parse( CM.AppSettings["InboundExploratoryTunnels"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["ExploratoryTunnelHops"] ) )
            {
                DefaultExploratoryTunnelHopCount = int.Parse( CM.AppSettings["ExploratoryTunnelHops"] );
            }
        }

        internal int InboundTunnelsNeeded
        {
            get
            {
                return TargetInboundExploratoryTunnelCount -
                    Tunnels.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            }
        }

        internal int OutboundTunnelsNeeded
        {
            get
            {
                return TargetOutboundExploratoryTunnelCount -
                    Tunnels.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );
            }
        }

        private Tunnel CreateExploratoryOutboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.Exploratory,
                Tunnel.CreateOutboundTunnelChain( DefaultExploratoryTunnelHopCount, true ) );

            var tunnel = TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                Tunnels[tunnel] = false;
                TunnelMgr.AddTunnel( (OutboundTunnel)tunnel );
            }
            return tunnel;
        }

        private Tunnel CreateExploratoryInboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Exploratory,
                Tunnel.CreateInboundTunnelChain( DefaultExploratoryTunnelHopCount, true ) );

            var tunnel = TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                Tunnels[tunnel] = false;
                TunnelMgr.AddTunnel( (InboundTunnel)tunnel );
            }
            return tunnel;
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TimeBetweenTunnelBuilds );
        PeriodicAction LogStatus = new PeriodicAction( TickSpan.Seconds( 30 ) );

        public void Execute()
        {
            TunnelBuild.Do( BuildNewTunnels );
            LogStatus.Do( LogStatusReport );
        }

        private void LogStatusReport()
        {
            var ei = Tunnels.Count( t => t.Value && t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var pi = Tunnels.Count( t => !t.Value && t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var eo = Tunnels.Count( t => t.Value && t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );
            var po = Tunnels.Count( t => !t.Value && t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );

            Logging.LogInformation(
                $"Established explor  tunnels in: {ei,2} ( {pi,2} ), out: {eo,2} ( {po,2} ) {ExplorationTunnelBuildSuccessRatio}" );
        }

        private void BuildNewTunnels()
        {
            while ( OutboundTunnelsNeeded > 0 )
            {
#if DEBUG
                var xo = Tunnels.Count( t =>
                        t.Value &&
                        t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );

                var xop = Tunnels.Count( t =>
                        !t.Value &&
                        t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );

                Logging.LogDebugData( $"Exploratory OutboundTunnelsNeeded: {OutboundTunnelsNeeded} " +
                    $"{xo} {xop}" );
#endif

                if ( CreateExploratoryOutboundTunnel() == null ) break;
            }

            while ( InboundTunnelsNeeded > 0 )
            {
#if DEBUG
                var xi = Tunnels.Count( t =>
                        t.Value &&
                        t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );

                var xip = Tunnels.Count( t =>
                        !t.Value &&
                        t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );

                Logging.LogDebugData( $"Exploratory InboundTunnelsNeeded: {InboundTunnelsNeeded} " +
                    $"{xi} {xip}" );
#endif

                if ( CreateExploratoryInboundTunnel() == null ) break;
            }
        }

        #region TunnelEvents

        public void TunnelEstablished( Tunnel tunnel )
        {
            ExplorationTunnelBuildSuccessRatio.Success();

            Tunnels[tunnel] = true;
        }

        public void TunnelBuildFailed( Tunnel tunnel, bool timeout )
        {
            ExplorationTunnelBuildSuccessRatio.Failure();

            Tunnels.TryRemove( tunnel, out _ );
        }

        public void TunnelExpired( Tunnel tunnel )
        {
            Tunnels.TryRemove( tunnel, out _ );
        }

        public void TunnelFailed( Tunnel tunnel )
        {
            TunnelExpired( tunnel );
        }

        #endregion

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
