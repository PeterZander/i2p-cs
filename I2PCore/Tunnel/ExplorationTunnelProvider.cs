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
        public int TargetOutboundExploratoryTunnelCount = 20;
        public int TargetInboundExploratoryTunnelCount = 20;
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
                return TargetInboundExploratoryTunnelCount - TunnelMgr.ExploratoryActiveInboundTunnelCount;
            }
        }

        internal int OutboundTunnelsNeeded
        {
            get
            {
                return TargetOutboundExploratoryTunnelCount - TunnelMgr.ExploratoryActiveOutboundTunnelCount;
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

        private void CreateExploratoryOutboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.Exploratory,
                CreateOutgoingTunnelChain() );

            var tunnel = TunnelMgr.CreateTunnel( config );
            if ( tunnel != null ) TunnelMgr.AddTunnel( (OutboundTunnel)tunnel );
        }

        private void CreateExploratoryInboundTunnel()
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Exploratory,
                CreateIncommingTunnelChain() );

            var tunnel = TunnelMgr.CreateTunnel( config );
            if ( tunnel != null ) TunnelMgr.AddTunnel( (InboundTunnel)tunnel );
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TimeBetweenTunnelBuilds );

        internal void Execute()
        {
            TunnelBuild.Do( () => BuildNewTunnels() );
        }

        private void BuildNewTunnels()
        {
            if ( OutboundTunnelsNeeded > 0 )
            {
                CreateExploratoryOutboundTunnel();
            }

            if ( InboundTunnelsNeeded > 0 )
            {
                CreateExploratoryInboundTunnel();
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
