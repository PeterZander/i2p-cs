using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Data;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP.Messages;
using System.Collections.Concurrent;

namespace I2PCore.TunnelLayer
{
    public class ClientTunnelProvider: ITunnelOwner
    {
        public static double NewTunnelCreationFactor = 1.5;

        List<IClient> Clients = new List<IClient>();

        ConcurrentDictionary<Tunnel, IClient> PendingTunnels =
            new ConcurrentDictionary<Tunnel, IClient>();

        ConcurrentDictionary<Tunnel, IClient> Destinations = 
            new ConcurrentDictionary<Tunnel, IClient>();

        ConcurrentDictionary<Tunnel, TunnelUnderReplacement> RunningReplacements = 
            new ConcurrentDictionary<Tunnel, TunnelUnderReplacement>();

        internal TunnelProvider TunnelMgr;
        public SuccessRatio ClientTunnelBuildSuccessRatio = new SuccessRatio();

        protected class TunnelUnderReplacement
        {
            internal TunnelUnderReplacement( Tunnel old, IClient dest ) 
            { 
                OldTunnel = old;
                Client = dest;
            }

            internal readonly Tunnel OldTunnel;
            internal readonly IClient Client;

            internal List<Tunnel> NewTunnels = new List<Tunnel>();
        }

        internal ClientTunnelProvider( TunnelProvider tp )
        {
            TunnelMgr = tp;
        }

        internal void AttachClient( IClient client )
        {
            lock ( Clients )
            {
                Clients.Add( client );
            }
        }

        internal void DetachClient( IClient client )
        {
            lock ( Clients )
            {
                Clients.Remove( client );
            }
        }

        private OutboundTunnel CreateOutboundTunnel( IClient client, TunnelInfo prototype )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.Client,
                prototype ?? Tunnel.CreateOutboundTunnelChain( client.OutboundTunnelHopCount, false ) );

            var tunnel = (OutboundTunnel)TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                TunnelMgr.AddTunnel( tunnel );
                client.AddOutboundPending( tunnel );
                PendingTunnels[tunnel] = client;
            }
            return tunnel;
        }

        private InboundTunnel CreateInboundTunnel( IClient client, TunnelInfo prototype )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Client,
                prototype ?? Tunnel.CreateInboundTunnelChain( client.InboundTunnelHopCount, false ) );

            var tunnel = (InboundTunnel)TunnelMgr.CreateTunnel( this, config );
            if ( tunnel != null )
            {
                TunnelMgr.AddTunnel( tunnel );
                client.AddInboundPending( tunnel );
                PendingTunnels[tunnel] = client;
            }
            return tunnel;
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TickSpan.Seconds( 1 ) );
        PeriodicAction DestinationExecute = new PeriodicAction( TickSpan.Seconds( 1 ) );
        PeriodicAction ReplaceTunnels = new PeriodicAction( TickSpan.Seconds( 5 ) );
        PeriodicAction LogStatus = new PeriodicAction( TickSpan.Seconds( 20 ) );

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
                var dests = Destinations.Select( d => d.Value ).ToArray();

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
            var dti = Destinations.Where( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var pti = PendingTunnels.Where( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Inbound );
            var dto = Destinations.Where( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );
            var pto = PendingTunnels.Where( t => t.Key.Config.Direction == TunnelConfig.TunnelDirection.Outbound );

            var ei = dti.Count();
            var pi = pti.Count();
            var eo = dto.Count();
            var po = pto.Count();

            var post = "";
            var pist = "";

#if LOG_ALL_TUNNEL_TRANSFER
            pist = string.Join( ", ", pti.Select( t => t.Key.TunnelDebugTrace ) );
            post = string.Join( ", ", pto.Select( t => t.Key.TunnelDebugTrace ) );
#endif

            Logging.LogInformation(
                $"Established client tunnels in : {ei,2} ( {pi,2} {pist}), out: {eo,2} ( {po,2} {post}) {ClientTunnelBuildSuccessRatio}" );
        }

        class TunnelsNeededInfo
        {
            internal IClient Client;
            internal int TunnelsNeeded;
        }

        private void BuildNewTunnels()
        {
            TunnelsNeededInfo[] tocreateinbound;
            TunnelsNeededInfo[] tocreateoutbound;

            lock ( Clients )
            {
                tocreateinbound = Clients.Where( c => c.InboundTunnelsNeeded > 0 ).
                    Select( c => new TunnelsNeededInfo() { Client = c, TunnelsNeeded = c.InboundTunnelsNeeded } ).ToArray();

                tocreateoutbound = Clients.Where( c => c.OutboundTunnelsNeeded > 0 ).
                    Select( c => new TunnelsNeededInfo() { Client = c, TunnelsNeeded = c.OutboundTunnelsNeeded } ).ToArray();

            }

            foreach ( var create in tocreateinbound )
            {
                var needed = create.TunnelsNeeded * NewTunnelCreationFactor;
                for ( int i = 0; i < needed; ++i )
                {
                    Logging.LogDebug( $"{this} building new inbound tunnel {i + 1}/{needed}" );

                    var t = CreateInboundTunnel( create.Client, null );
                    if ( t == null )
                    {
                        // No outbound tunnels available
                        TunnelBuild.TimeToAction = TickSpan.Seconds( 10 );
                        break;
                    }
                }
            }

            foreach ( var create in tocreateoutbound )
            {
                var needed = create.TunnelsNeeded * NewTunnelCreationFactor;
                for ( int i = 0; i < create.TunnelsNeeded * NewTunnelCreationFactor; ++i )
                {
                    Logging.LogDebug( $"{this} building new outbound tunnel {i + 1}/{needed}" );
                    CreateOutboundTunnel( create.Client, null );
                }
            }

            TunnelMgr.ClientTunnelsStatusOk = Clients.All( ct => ct.ClientTunnelsStatusOk );
        }

        #region TunnelEvents
        public void TunnelEstablished( Tunnel tunnel )
        {
            TunnelUnderReplacement replace = null;

            ClientTunnelBuildSuccessRatio.Success();

            if ( !PendingTunnels.TryRemove( tunnel, out var client ) )
            {
                Logging.LogDebug( $"ClientTunnelProvider: WARNING. Unable to find client for established tunnel {tunnel}" );
                return;
            }
            Destinations[tunnel] = client;

            try
            {
                client.TunnelEstablished( tunnel );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }

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

        public void TunnelBuildFailed( Tunnel tunnel, bool timeout )
        {
            ClientTunnelBuildSuccessRatio.Failure();

            if ( !PendingTunnels.TryRemove( tunnel, out var client ) )
            {
                Logging.LogDebug( $"ClientTunnelProvider: WARNING. Unable to find client TunnelBuildTimeout! {tunnel}" );
                return;
            }

            try
            {
                client.RemoveTunnel( tunnel );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }

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
            if ( !Destinations.TryGetValue( tunnel, out var client ) )
            {
                Logging.LogDebug( $"ClientTunnelProvider: WARNING. Unable to find client for TunnelReplacementNeeded {tunnel}" );
                return;
            }

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
            if ( !Destinations.TryRemove( tunnel, out var client ) )
            {
                Logging.LogDebug( $"ClientTunnelProvider: WARNING. Unable to find client for TunnelExpired {tunnel}" );
                return;
            }

            try
            {
                client.RemoveTunnel( tunnel );
            }
            catch( Exception ex )
            {
                Logging.Log( ex );
            }

            var replace = FindReplaceRecord( tunnel );

            if ( replace != null )
            {
                Logging.LogDebug( $"ClientTunnelProvider: TunnelTimeout: Failed replacing {replace.OldTunnel }" +
                    $" with {tunnel}" );

                /*
                if ( replace.OldTunnel.Expired )
                {
                    Logging.LogDebug( "ClientTunnelProvider: TunnelTimeout: Old tunnel expired. " + replace.OldTunnel.ToString() );

                    client.RemovePoolTunnel( replace.OldTunnel );
                }*/

                ReplaceTunnel( tunnel, client, replace );
            }
        }
        #endregion

        private void ReplaceTunnel( Tunnel tunnel, IClient client, TunnelUnderReplacement replace )
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

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
