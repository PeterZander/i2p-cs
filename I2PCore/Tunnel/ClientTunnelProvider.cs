using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Data;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Tunnel.I2NP
{
    public class ClientTunnelProvider
    {
        const int NewTunnelCreationFactor = 2;

        List<ClientDestination> Clients = new List<ClientDestination>();
        Dictionary<Tunnel, ClientDestination> Destinations = new Dictionary<Tunnel, ClientDestination>();
        Dictionary<Tunnel, TunnelUnderReplacement> RunningReplacements = new Dictionary<Tunnel, TunnelUnderReplacement>();

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

            var tunnel = (OutboundTunnel)TunnelMgr.CreateTunnel( config );
            if ( tunnel != null )
            {
                TunnelMgr.AddTunnel( tunnel );
                dest.AddOutboundPending( tunnel );
                lock ( Destinations ) Destinations[tunnel] = dest;
            }
            return tunnel;
        }

        private InboundTunnel CreateInboundTunnel( ClientDestination dest, TunnelInfo prototype )
        {
            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Client,
                prototype ?? CreateIncommingTunnelChain( dest ) );

            var tunnel = (InboundTunnel)TunnelMgr.CreateTunnel( config );
            if ( tunnel != null )
            {
                tunnel.GarlicMessageReceived += new Action<GarlicMessage>( GarlicMessageReceived );
                TunnelMgr.AddTunnel( tunnel );
                dest.AddInboundPending( tunnel );
                lock ( Destinations ) Destinations[tunnel] = dest;
            }
            return tunnel;
        }

        PeriodicAction TunnelBuild = new PeriodicAction( TickSpan.Seconds( 2 ) );
        PeriodicAction DestinationExecute = new PeriodicAction( TickSpan.Seconds( 1 ) );

        internal void Execute()
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

                lock ( Destinations )
                {
                    dests = Destinations.Select( d => d.Value ).ToArray();
                }

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
            } );
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
                for ( int i = 0; i < create.TunnelsNeeded * NewTunnelCreationFactor; ++i )
                {
                    var t = CreateInboundTunnel( create.Destination, null );
                }
            }

            foreach ( var create in tocreateoutbound )
            {
                for ( int i = 0; i < create.TunnelsNeeded * NewTunnelCreationFactor; ++i )
                {
                    var t = CreateOutboundTunnel( create.Destination, null );
                }
            }

            ClientTunnelsStatusOk = Clients.All( ct => ct.ClientTunnelsStatusOk );
        }

        internal bool ClientTunnelsStatusOk { get; private set; }

        #region TunnelEvents
        internal void TunnelEstablished( Tunnel tunnel )
        {
            TunnelUnderReplacement replace = null;

            ClientDestination client = FindClient( tunnel );
            if ( client == null ) return;

            client.TunnelEstablished( tunnel );

            lock ( RunningReplacements )
            {
                replace = RunningReplacements.Where( p => p.Value.NewTunnels.Any( t => t.Equals( tunnel ) ) ).
                    Select( p => p.Value ).SingleOrDefault();
                if ( replace != null ) RunningReplacements.Remove( replace.OldTunnel );
            }

            if ( replace != null )
            {
                Logging.LogDebug( $"ClientTunnelProvider: TunnelEstablished: Successfully replaced old tunnel {replace.OldTunnel} with new tunnel {tunnel}" );

                RemoveTunnelUnderReplacement( replace.OldTunnel );
                //client.RemoveTunnel( replace.OldTunnel );
                //TunnelMgr.RemoveTunnel( replace.OldTunnel );
                //replace.OldTunnel.Shutdown();
                return;
            }
        }

        internal void TunnelBuildTimeout( Tunnel tunnel )
        {
            ClientDestination client = FindClient( tunnel );
            if ( client == null ) return;

            client.RemoveTunnel( tunnel );

            lock ( Destinations )
            {
                Destinations.Remove( tunnel );
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

        internal void TunnelReplacementNeeded( Tunnel tunnel )
        {
            ClientDestination client = FindClient( tunnel );
            if ( client == null ) return;

            TunnelUnderReplacement replace;

            lock ( RunningReplacements )
            {
                if ( !RunningReplacements.TryGetValue( tunnel, out replace ) ) return; // Already being replaced

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
                lock ( RunningReplacements )
                {
                    RunningReplacements[tunnel] = replace;
                }
            }

            ReplaceTunnel( tunnel, client, replace );
        }

        internal void TunnelTimeout( Tunnel tunnel )
        {
            ClientDestination client = FindClient( tunnel );
            if ( client == null ) return;

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

        private ClientDestination FindClient( Tunnel tunnel )
        {
            ClientDestination client;

            lock ( Destinations )
            {
                if ( !Destinations.TryGetValue( tunnel, out client ) )
                {
                    Logging.LogDebug( $"ClientTunnelProvider: TunnelEstablished: " +
                        $"Unable to find a matching Destination for {tunnel}" );
                    return null;
                }
            }

            return client;
        }

        private TunnelUnderReplacement FindReplaceRecord( Tunnel newtunnel )
        {
            TunnelUnderReplacement replace;
            lock ( RunningReplacements )
            {
                replace = RunningReplacements.Where( p => p.Value.NewTunnels.Any( t => t.Equals( newtunnel ) ) ).
                    Select( p => p.Value ).SingleOrDefault();
            }
            return replace;
        }

        private void RemoveTunnelUnderReplacement( Tunnel tunnel )
        {
            lock ( RunningReplacements )
            {
                RunningReplacements.Remove( tunnel );
            }
        }

        #endregion

        #region Client communication

        void GarlicMessageReceived( GarlicMessage msg )
        {
        }

        #endregion
    }
}
