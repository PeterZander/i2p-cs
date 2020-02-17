using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Data;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;
using System.Collections.Concurrent;

namespace I2PCore.Tunnel
{
    public class ClientTunnelProvider: ITunnelOwner
    {
        const int NewTunnelCreationFactor = 2;

        List<ClientDestination> Clients = new List<ClientDestination>();

        ConcurrentDictionary<Tunnel, ClientDestination> PendingTunnels =
            new ConcurrentDictionary<Tunnel, ClientDestination>();

        ConcurrentDictionary<Tunnel, ClientDestination> Destinations = 
            new ConcurrentDictionary<Tunnel, ClientDestination>();

        ConcurrentDictionary<Tunnel, TunnelUnderReplacement> RunningReplacements = 
            new ConcurrentDictionary<Tunnel, TunnelUnderReplacement>();

        internal TunnelProvider TunnelMgr;

        protected class TunnelUnderReplacement
        {
            internal TunnelUnderReplacement( Tunnel old, ClientDestination dest ) 
            { 
                OldTunnel = old;
                Destination = dest;
            }

            internal readonly Tunnel OldTunnel;
            internal readonly ClientDestination Destination;

            internal List<Tunnel> NewTunnels = new List<Tunnel>();
        }

        internal ClientTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
        }

        internal ClientDestination CreateDestination( I2PDestinationInfo dest, bool publish )
        {
            var newclient = new ClientDestination( this, dest, publish );
            Clients.Add( newclient );
            return newclient;
        }

        TunnelInfo CreateOutgoingTunnelChain( ClientDestination dest )
        {
            var hops = new List<HopInfo>();

            for ( int i = 0; i < dest.OutboundTunnelHopCount; ++i )
            {
                var ih = NetDb.Inst.GetRandomRouterForTunnelBuild( false );
                if ( ih is null ) return new TunnelInfo( hops );

                hops.Add( new HopInfo( NetDb.Inst[ih].Identity, new I2PTunnelId() ) );
            }

            return new TunnelInfo( hops );
        }

        TunnelInfo CreateIncommingTunnelChain( ClientDestination dest )
        {
            var hops = new List<HopInfo>();

            for ( int i = 0; i < dest.InboundTunnelHopCount; ++i )
            {
                var ih = NetDb.Inst.GetRandomRouterForTunnelBuild( false );
                if ( ih is null ) return new TunnelInfo( hops );

                hops.Add( new HopInfo( NetDb.Inst[ih].Identity, new I2PTunnelId() ) );
            }
            hops.Add( new HopInfo( RouterContext.Inst.MyRouterIdentity, new I2PTunnelId() ) );

            return new TunnelInfo( hops );
        }

        private OutboundTunnel CreateOutboundTunnel( ClientDestination dest, TunnelInfo prototype )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.Client,
                prototype ?? CreateOutgoingTunnelChain( dest ) );

            var tunnel = (OutboundTunnel)TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                TunnelMgr.AddTunnel( tunnel );
                dest.AddOutboundPending( tunnel );
                PendingTunnels[tunnel] = dest;
            }
            return tunnel;
        }

        private InboundTunnel CreateInboundTunnel( ClientDestination dest, TunnelInfo prototype )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Client,
                prototype ?? CreateIncommingTunnelChain( dest ) );

            var tunnel = (InboundTunnel)TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                tunnel.GarlicMessageReceived += new Action<GarlicMessage>( GarlicMessageReceived );
                TunnelMgr.AddTunnel( tunnel );
                dest.AddInboundPending( tunnel );
                PendingTunnels[tunnel] = dest;
            }
            return tunnel;
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TickSpan.Seconds( 1 ) );
        PeriodicAction DestinationExecute = new PeriodicAction( TickSpan.Seconds( 1 ) );
        PeriodicAction ReplaceTunnels = new PeriodicAction( TickSpan.Seconds( 5 ) );
        PeriodicAction LogStatus = new PeriodicAction( TickSpan.Seconds( 10 ) );

        public void Execute()
        {
            TunnelBuild.Do( () => 
            {
                try
                {
                    BuildNewTunnels();
                }
                catch ( Exception ex )
                {
                    Logging.Log( "ClientTunnelProvider Execute BuildNewTunnels", ex );
                }
            });

            DestinationExecute.Do( () =>
            {
                ClientDestination[] dests;

                dests = Destinations.Select( d => d.Value ).ToArray();

                foreach ( var onedest in dests )
                {
                    try
                    {
                        onedest.Execute();
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( "ClientTunnelProvider Execute DestExecute", ex );
                    }
                }

                ReplaceTunnels.Do( CheckForTunnelReplacementTimeout );

                LogStatus.Do( LogStatusReport );
            } );
        }

        private void LogStatusReport()
        {
            var ei = Destinations.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var pi = PendingTunnels.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var eo = Destinations.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );
            var po = PendingTunnels.Count( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );

            Logging.LogInformation(
                $"Established client tunnels in : {ei,2} ( {pi,2} ), out: {eo,2} ( {po,2} )" );
        }

        class TunnelsNeededInfo
        {
            internal ClientDestination Destination;
            internal int TunnelsNeeded;
        }

        private void BuildNewTunnels()
        {
            TunnelsNeededInfo[] tocreateinbound;
            TunnelsNeededInfo[] tocreateoutbound;

            lock ( Clients )
            {
                tocreateinbound = Clients.Where( c => c.InboundTunnelsNeeded > 0 ).
                    Select( c => new TunnelsNeededInfo() { Destination = c, TunnelsNeeded = c.InboundTunnelsNeeded } ).ToArray();

                tocreateoutbound = Clients.Where( c => c.OutboundTunnelsNeeded > 0 ).
                    Select( c => new TunnelsNeededInfo() { Destination = c, TunnelsNeeded = c.OutboundTunnelsNeeded } ).ToArray();

            }

            foreach ( var create in tocreateinbound )
            {
                var needed = create.TunnelsNeeded * NewTunnelCreationFactor;
                for ( int i = 0; i < needed; ++i )
                {
                    Logging.LogDebug( $"{this} building new inbound tunnel {i + 1}/{needed}" );

                    var t = CreateInboundTunnel( create.Destination, null );
                    if ( t == null )
                    {
                        // No outbound tunnels available
                        TunnelBuild.TimeToAction = TickSpan.Seconds( 10 );
                    }
                }
            }

            foreach ( var create in tocreateoutbound )
            {
                var needed = create.TunnelsNeeded * NewTunnelCreationFactor;
                for ( int i = 0; i < create.TunnelsNeeded * NewTunnelCreationFactor; ++i )
                {
                    Logging.LogDebug( $"{this} building new outbound tunnel {i + 1}/{needed}" );
                    CreateOutboundTunnel( create.Destination, null );
                }
            }

            TunnelMgr.ClientTunnelsStatusOk = Clients.All( ct => ct.ClientTunnelsStatusOk );
        }

        #region TunnelEvents
        public void TunnelEstablished( Tunnel tunnel )
        {
            TunnelUnderReplacement replace = null;

            PendingTunnels.TryRemove( tunnel, out var client );
            Destinations[tunnel] = client;

            client.TunnelEstablished( tunnel );

            replace = RunningReplacements.Where( p => 
                p.Value.NewTunnels.Any( 
                    t => t.Equals( tunnel ) ) )
                .Select( p => p.Value )
                .FirstOrDefault();

            if ( replace != null ) RunningReplacements.TryRemove( replace.OldTunnel, out _ );

            if ( replace != null )
            {
                Logging.LogDebug( $"ClientTunnelProvider: TunnelEstablished: Successfully replaced old tunnel {replace.OldTunnel} with new tunnel {tunnel}" );

                RunningReplacements.TryRemove( replace.OldTunnel, out _ );
                return;
            }
        }

        public void TunnelBuildTimeout( Tunnel tunnel )
        {
            if ( !PendingTunnels.TryRemove( tunnel, out var client ) ) return;
            client.RemoveTunnel( tunnel );

            var replace = FindReplaceRecord( tunnel );

            if ( replace != null )
            {
                Logging.LogDebug( $"ClientTunnelProvider: TunnelBuildTimeout: " +
                    $"Failed replacing {replace.OldTunnel} with {tunnel}" );

                if ( replace.OldTunnel.Expired )
                {
                    Logging.LogDebug( $"ClientTunnelProvider: TunnelBuildTimeout: " +
                        $"Old tunnel expired. {replace.OldTunnel}" );

                    client.RemoveTunnel( replace.OldTunnel );
                }

                tunnel.Shutdown();
                ReplaceTunnel( tunnel, client, replace );
            }
            else
            {
/*
                Logging.LogDebug( $"ClientTunnelProvider: TunnelBuildTimeout: " +
                    $"Unable to find a matching tunnel under replacement for {tunnel}" );
 */
            }
        }

        protected void TunnelReplacementNeeded( Tunnel tunnel )
        {
            if ( !Destinations.TryGetValue( tunnel, out var client ) ) return;

            if ( !RunningReplacements.TryGetValue( tunnel, out var replace ) ) return; // Already being replaced

            // Too many tunnels already?
            if ( tunnel is InboundTunnel )
            {
                if ( client.InboundTunnelsNeeded < 0 ) return;
            }
            else
            {
                if ( client.OutboundTunnelsNeeded < 0 ) return;
            }

            replace = new TunnelUnderReplacement( tunnel, client );
            RunningReplacements[tunnel] = replace;

            ReplaceTunnel( tunnel, client, replace );
        }

        public void TunnelFailed( Tunnel tunnel )
        {
            TunnelExpired( tunnel );
        }

        public void TunnelExpired( Tunnel tunnel )
        {
            if ( !PendingTunnels.TryGetValue( tunnel, out var client ) ) return;

            var replace = FindReplaceRecord( tunnel );

            if ( replace != null )
            {
                Logging.LogDebug( "ClientTunnelProvider: TunnelTimeout: Failed replacing " + replace.OldTunnel.ToString() +
                    " with " + tunnel.ToString() );

                /*
                if ( replace.OldTunnel.Expired )
                {
                    Logging.LogDebug( "ClientTunnelProvider: TunnelTimeout: Old tunnel expired. " + replace.OldTunnel.ToString() );

                    client.RemovePoolTunnel( replace.OldTunnel );
                }*/

                ReplaceTunnel( tunnel, client, replace );
            }

            client.RemoveTunnel( tunnel );
        }
        #endregion

        private void ReplaceTunnel( Tunnel tunnel, ClientDestination client, TunnelUnderReplacement replace )
        {
            if ( tunnel != replace.OldTunnel )
            {
                replace.NewTunnels.RemoveAll( t => t.Equals( tunnel ) );
            }

            while ( replace.NewTunnels.Count < NewTunnelCreationFactor )
            {
                switch ( tunnel.Config.Direction )
                {
                    case TunnelConfig.TunnelDirection.Outbound:
                        var newouttunnel = CreateOutboundTunnel( client, null );
                        replace.NewTunnels.Add( newouttunnel );
                        Logging.LogDebug( () => string.Format( "ClientTunnelProvider: ReplaceTunnel: Started to replace {0} with {1}.",
                            tunnel, newouttunnel ) );
                        break;

                    case TunnelConfig.TunnelDirection.Inbound:
                        var newintunnel = CreateInboundTunnel( client, null );
                        replace.NewTunnels.Add( newintunnel );
                        Logging.LogDebug( () => string.Format( "ClientTunnelProvider: ReplaceTunnel: Started to replace {0} with {1}.",
                            tunnel, newintunnel ) );
                        break;

                    default:
                        throw new NotImplementedException( "Only out and inbound tunnels should be reported here." );
                }
            }
        }

        #region Queue mgmt

        private TunnelUnderReplacement FindReplaceRecord( Tunnel newtunnel )
        {
            var replace = RunningReplacements
                    .Where( p => 
                        p.Value.NewTunnels.Any( t => t.Equals( newtunnel ) ) )
                    .Select( p => p.Value )
                    .FirstOrDefault();

            return replace;
        }

        #endregion

        #region Client communication

        void GarlicMessageReceived( GarlicMessage msg )
        {
        }

        #endregion

        private void CheckForTunnelReplacementTimeout()
        {
            IEnumerable<Tunnel> timeoutlist;

            timeoutlist = Destinations
                .Where( t => t.Key.NeedsRecreation )
                .Select( t => t.Key )
                .ToArray();

            foreach ( var one in timeoutlist )
            {
                /*
                Logging.LogDebug( "TunnelProvider: CheckForTunnelReplacementTimeout: " + one.Pool.ToString() + 
                    " tunnel " + one.TunnelDebugTrace + " needs to be replaced." );
                 */
                TunnelReplacementNeeded( one );
            }
        }

    }
}
