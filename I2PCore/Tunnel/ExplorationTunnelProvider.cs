using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Data;
using I2PCore.Router;

namespace I2PCore.Tunnel
{
    internal class ExplorationTunnelProvider
    {
        public int TargetOutboundExploratoryTunnelCount = 10;
        public int TargetInboundExploratoryTunnelCount = 10;
        public int DefaultExploratoryTunnelHopCount = 1;
        public static readonly TickSpan TimeBetweenTunnelBuilds = TickSpan.Seconds( 2 );

        TunnelProvider TunnelMgr;

        internal ExplorationTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
        }

        internal int InboundTunnelsNeeded
        {
            get
            {
                return TargetInboundExploratoryTunnelCount - 
                    ( TunnelMgr.ExploratoryInboundTunnelCount 
                        + TunnelMgr.ExploratoryPendingInboundTunnelCount );
            }
        }

        internal int OutboundTunnelsNeeded
        {
            get
            {
                return TargetOutboundExploratoryTunnelCount - 
                    ( TunnelMgr.ExploratoryOutboundTunnelCount
                        + TunnelMgr.ExploratoryPendingOutboundTunnelCount );
            }
        }

        TunnelInfo CreateOutgoingTunnelChain()
        {
            var hops = new List<HopInfo>();

            for ( int i = 0; i < DefaultExploratoryTunnelHopCount; ++i )
            {
                var ih = NetDb.Inst.GetRandomRouterForTunnelBuild( true );
                hops.Add( new HopInfo( NetDb.Inst[ih].Identity, new I2PTunnelId() ) );
            }

            return new TunnelInfo( hops );
        }

        TunnelInfo CreateIncommingTunnelChain()
        {
            var hops = new List<HopInfo>();

            for ( int i = 0; i < DefaultExploratoryTunnelHopCount; ++i )
            {
                var ih = NetDb.Inst.GetRandomRouterForTunnelBuild( true );
                hops.Add( new HopInfo( NetDb.Inst[ih].Identity, new I2PTunnelId() ) );
            }
            hops.Add( new HopInfo( RouterContext.Inst.MyRouterIdentity, new I2PTunnelId() ) );

            return new TunnelInfo( hops );
        }

        private Tunnel CreateExploratoryOutboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.Exploratory,
                CreateOutgoingTunnelChain() );

            var tunnel = TunnelMgr.CreateTunnel( config );
            if ( tunnel != null ) TunnelMgr.AddTunnel( (OutboundTunnel)tunnel );
            return tunnel;
        }

        private Tunnel CreateExploratoryInboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Exploratory,
                CreateIncommingTunnelChain() );

            var tunnel = TunnelMgr.CreateTunnel( config );
            if ( tunnel != null ) TunnelMgr.AddTunnel( (InboundTunnel)tunnel );
            return tunnel;
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TimeBetweenTunnelBuilds );

        internal void Execute()
        {
            TunnelBuild.Do( BuildNewTunnels );
        }

        private void BuildNewTunnels()
        {
            while ( OutboundTunnelsNeeded > 0 )
            {
                Logging.LogDebugData( $"Exploratory OutboundTunnelsNeeded: {OutboundTunnelsNeeded} " +
                    $"{TunnelMgr.ExploratoryOutboundTunnelCount} {TunnelMgr.ExploratoryPendingOutboundTunnelCount}" );
                if ( CreateExploratoryOutboundTunnel() == null ) break;
            }

            while ( InboundTunnelsNeeded > 0 )
            {
                Logging.LogDebugData( $"Exploratory InboundTunnelsNeeded: {InboundTunnelsNeeded} " +
                    $"{TunnelMgr.ExploratoryInboundTunnelCount} {TunnelMgr.ExploratoryPendingInboundTunnelCount}" );
                if ( CreateExploratoryInboundTunnel() == null ) break;
            }
        }

        #region TunnelEvents
        internal void TunnelEstablished( Tunnel tunnel )
        {
        }

        internal void TunnelBuildTimeout( Tunnel tunnel )
        {
        }

        internal void TunnelTimeout( Tunnel tunnel )
        {
        }
        #endregion

        #region Queue mgmt

        #endregion
    }
}
