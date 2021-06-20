#define RUN_TUNNEL_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.Threading;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TransportLayer;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace I2PCore.TunnelLayer
{
    public class TunnelProvider
    {
        public static TunnelProvider Inst { get; protected set; }

        /// <summary>
        /// Tunnel build requests that is not a known Inbound or Outbound tunnel.
        /// </summary>
        public event Action<II2NPHeader, TunnelBuildRequestDecrypt>
            TunnelBuildRequestEvents;

        /// <summary>
        /// Non-tunnel build messages received.
        /// </summary>
        public static event Action<II2NPHeader> I2NPMessageReceived;

        private const double TunnelSelectionElitism = 5.0;

        // The byte value have no real meaning, the dictionaries are hash sets.
        ConcurrentDictionary<InboundTunnel, byte> PendingInbound = new ConcurrentDictionary<InboundTunnel, byte>();
        ConcurrentDictionary<OutboundTunnel,byte> PendingOutbound = new ConcurrentDictionary<OutboundTunnel, byte>();

        ConcurrentDictionary<InboundTunnel, byte> EstablishedInbound = new ConcurrentDictionary<InboundTunnel, byte>();
        ConcurrentDictionary<OutboundTunnel, byte> EstablishedOutbound = new ConcurrentDictionary<OutboundTunnel, byte>();

        readonly TunnelIdSubsriptions TunnelIds = new TunnelIdSubsriptions();

        protected static Thread Worker;

        ConcurrentQueue<II2NPHeader> IncomingMessageQueue = new ConcurrentQueue<II2NPHeader>();

        protected static Thread IncomingMessagePump;

        internal bool ClientTunnelsStatusOk { get; set; }
        internal bool AcceptTransitTunnels { get => ClientTunnelsStatusOk; }

        TunnelProvider()
        {
            Worker = new Thread( Run )
            {
                Name = "TunnelProvider",
                IsBackground = true
            };
            Worker.Start();

            IncomingMessagePump = new Thread( RunIncomingMessagePump )
            {
                Name = "TunnelProvider IncomingMessagePump",
                IsBackground = true
            };
            IncomingMessagePump.Start();

            TransportProvider.Inst.IncomingMessage += new Action<ITransport,II2NPHeader>( DistributeIncomingMessage );
        }

        public static void Start()
        {
            if ( Inst != null ) return;
            Inst = new TunnelProvider();
        }

        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 30 ) );
        PeriodicAction TunnelBandwidthLog = new PeriodicAction( TickSpan.Minutes( 8 ) );

        PeriodicAction CheckTunnelTimeouts = new PeriodicAction( Tunnel.ExpectedTunnelBuildTimePerHop );

        bool Terminated = false;
        private void Run()
        {
            try
            {
                Thread.Sleep( 2000 );

                while ( !Terminated )
                {
                    try
                    {
                        TunnelBandwidthLog.Do( () => ThreadPool.QueueUserWorkItem( cb =>
                        {
                            var tunnels = EstablishedInbound
                                    .Keys
                                    .ToArray()
                                    .Cast<Tunnel>()
                                    .Concat( EstablishedOutbound.Keys )
                                    .ToArray();

                            LogTunnelBandwidth( tunnels );
                        } ) );

                        QueueStatusLog.Do( () =>
                        {
                            var zh = EstablishedInbound.Count( t => t.Key is ZeroHopTunnel );
                            Logging.LogInformation(
                                $"Established 0-hop tunnels     : {zh,2}" );

                            Logging.LogInformation(
                                $"Tunnel data: {Tunnel.BandwidthTotal}" );

                            Logging.LogDebug( string.Format(
                                "Unresolvable routers: {0}. Unresolved routers: {1}. IP addresses with execptions: {2}. SSU blocked IPs: {3}.",
                                TransportProvider.Inst.CurrentlyUnresolvableRoutersCount,
                                TransportProvider.Inst.CurrentlyUnknownRoutersCount,
                                TransportProvider.Inst.AddressesWithExceptionsCount,
                                TransportProvider.Inst.SsuHostBlockedIPCount
                                ) );
                        } );

                        CheckTunnelTimeouts.Do( CheckForTunnelBuildTimeout );

                        ExecuteQueue(
                            PendingOutbound.Select( d => d.Key ),
                            ( t ) => PendingOutbound.TryRemove( (OutboundTunnel)t, out _ ),
                            true );

                        ExecuteQueue(
                            PendingInbound.Select( d => d.Key ),
                            ( t ) => PendingInbound.TryRemove( (InboundTunnel)t, out _ ),
                            true );

                        ExecuteQueue(
                            EstablishedOutbound.Select( d => d.Key ),
                            ( t ) => EstablishedOutbound.TryRemove( (OutboundTunnel)t, out _ ),
                            false );

                        ExecuteQueue(
                            EstablishedInbound.Select( d => d.Key ),
                            ( t ) => EstablishedInbound.TryRemove( (InboundTunnel)t, out _ ),
                            false );

                        Thread.Sleep( 1500 ); // Give data a chance to batch up
                    }
                    catch ( ThreadAbortException ex )
                    {
                        Logging.Log( ex );
                    }
                    catch ( Exception ex )
                    {
#if !LOG_ALL_TUNNEL_TRANSFER
                        if ( ex is IOException || ex is SocketException || ex is FailedToConnectException || ex is EndOfStreamEncounteredException )
                        {
                            Logging.Log( $"TransportProvider: Communication exception {ex.GetType()}" );
                        }
                        else
#endif
                        {
                            Logging.Log( ex );
                        }
                    }
                }
            }
            finally
            {
                Terminated = true;
            }
        }

        private void LogTunnelBandwidth( IEnumerable<Tunnel> tunnels )
        {
            foreach ( var tunnel in tunnels.OrderBy( t => t.TunnelDirection ).ThenBy( t => t.Pool ) )
            {
                if ( tunnel.Config.Direction == TunnelConfig.TunnelDirection.Inbound && tunnel.TunnelMembers.Any() )
                {
                    foreach ( var peer in tunnel.TunnelMembers.Select( id => id.IdentHash ).ToArray() )
                    {
                        NetDb.Inst.Statistics.MaxBandwidth( peer, tunnel.Bandwidth.ReceiveBandwidth );
                    }
                }

                // TODO: Revert when roslyn is fixed
                //Logging.LogInformation( $"Tunnel bandwidth {tunnel,-40} {tunnel.Bandwidth}" );
                Logging.LogInformation( $"Tunnel bandwidth {tunnel,40} {tunnel.Bandwidth}" );
            }
        }

        void CheckForTunnelBuildTimeout()
        {
            if ( PendingOutbound.Count > 0 )
            {
                CheckForTunnelBuildTimeout( PendingOutbound.Keys.ToArray() );
            }

            if ( PendingInbound.Count > 0 )
            {
                CheckForTunnelBuildTimeout( PendingInbound.Keys.ToArray() );
            }
        }

        private void CheckForTunnelBuildTimeout( IEnumerable<Tunnel> pool )
        {
            var timeout = pool.Where( t => 
                t.CreationTime.DeltaToNow > t.TunnelEstablishmentTimeout )
                    .ToArray();

#if DEBUG
            var removedtunnels = new List<Tunnel>();
#endif
            foreach ( var one in timeout )
            {
#if DEBUG
                removedtunnels.Add( one );
#endif
                one.Owner?.TunnelBuildFailed( one, true );

                foreach ( var dest in one.TunnelMembers )
                {
                    NetDb.Inst.Statistics.TunnelBuildTimeout( dest.IdentHash );
                }

                RemoveTunnel( one );
                one.Shutdown();
            }
#if DEBUG
            if ( removedtunnels.Any() )
            {
                var st = new StringBuilder();
                var pools = removedtunnels.GroupBy( t => t.Pool );
                foreach ( var onepool in pools )
                {
                    st.Append( $"{onepool.Key} {onepool.First().TunnelDirection}" );
                    foreach ( var one in pool )
                    {
                        st.Append( $"{one.TunnelDebugTrace} " );
                    }
                }

                Logging.LogDebug( $"TunnelProvider: Removing {st} due to establishment timeout." );
            }
#endif
        }

        readonly ConcurrentQueue<(Tunnel, bool)> FailedTunnels = new ConcurrentQueue<(Tunnel, bool)>();

        private void ExecuteQueue( IEnumerable<Tunnel> q, Action<Tunnel> remove, bool ispending )
        {
            RunTunnels( q );
            RemoveFailedTunnels( remove, ispending );
        }

        private void RunTunnels( IEnumerable<Tunnel> q )
        {
            foreach ( var tunnel in q )
            {
                try
                {
                    if ( tunnel.Terminated )
                    {
                        FailedTunnels.Enqueue( (tunnel, true) );
                        continue;
                    }

                    var ok = tunnel.Exectue();
                    if ( !ok )
                    {
                        FailedTunnels.Enqueue( (tunnel, true) );
                    }
                    else
                    {
                        if ( tunnel.Expired )
                        {
                            // Normal timeout
                            FailedTunnels.Enqueue( (tunnel, false) );
                            tunnel.Shutdown();
                        }
                    }
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug(
                        $"TunnelProvider: Exception in tunnel {tunnel} [{ex.GetType()}] '{ex.Message}'." );
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( ex );
#endif
                    FailedTunnels.Enqueue( (tunnel, true) );
                }
            }
        }

        private void RemoveFailedTunnels( Action<Tunnel> remove, bool ispending )
        {
            while ( FailedTunnels.TryDequeue( out var t ) )
            {
                var tunnel = t.Item1;
                var isfailed = t.Item2;

                if ( isfailed )
                {
                    if ( ispending )
                    {
                        Logging.LogDebug( $"TunnelProvider: ExecuteQueue removing failed tunnel {tunnel} during build." );
                        tunnel.Owner?.TunnelBuildFailed( tunnel, false );
                    }
                    else
                    {
                        Logging.LogDebug( $"TunnelProvider: ExecuteQueue removing failed tunnel {tunnel}." );
                        tunnel.Owner?.TunnelFailed( tunnel );
                    }

                    remove?.Invoke( tunnel );
                    RemoveTunnel( tunnel );
                }
                else
                {
                    Logging.LogDebug( $"TunnelProvider: ExecuteQueue removing expired tunnel {tunnel} created {tunnel.CreationTime}." );

                    tunnel.Owner?.TunnelExpired( tunnel );

                    remove?.Invoke( tunnel );
                    RemoveTunnel( tunnel );
                }
            }
        }

        public Tunnel CreateTunnel( ITunnelOwner owner, TunnelConfig config )
        {
            if ( config.Info.Hops.Count == 0 ) return null;

            if ( config.Direction == TunnelConfig.TunnelDirection.Outbound )
            {
                var replytunnel = GetInboundTunnel( true );
                var tunnel = new OutboundTunnel( owner, config, replytunnel.Config.Info.Hops.Count );

                var req = tunnel.CreateBuildRequest( replytunnel );

                Logging.LogDebug( $"TunnelProvider: {tunnel} created, build id: {tunnel.TunnelBuildReplyMessageId} {req.MessageId} {req.CreateHeader16.MessageId}." );

#if DEBUG
                ReallyOldTunnelBuilds.Set( tunnel.TunnelBuildReplyMessageId,
                    new RefPair<TickCounter, int>( TickCounter.Now, replytunnel.Config.Info.Hops.Count + config.Info.Hops.Count ) );
#endif

                TransportProvider.Send( tunnel.Destination, req );
                 
                return tunnel;
            }
            else
            {
                var outtunnel = GetEstablishedOutboundTunnel( true );
                if ( outtunnel == null )
                {
                    Logging.LogDebug( $"TunnelProvider: Inbound tunnel {config.Pool} " +
                        $"({config.Info.Hops.Count}): No establised outbound tunnels available." );

                    return null;
                }

                var tunnel = new InboundTunnel( owner, config, outtunnel.Config.Info.Hops.Count );

                Logging.LogDebug( $"TunnelProvider: Inbound tunnel {tunnel} created." );
#if DEBUG
                ReallyOldTunnelBuilds.Set( tunnel.TunnelBuildReplyMessageId,
                    new RefPair<TickCounter, int>( TickCounter.Now, outtunnel.Config.Info.Hops.Count + config.Info.Hops.Count ) );
#endif

                var tunnelbuild = tunnel.CreateBuildRequest();

                outtunnel.Send(
                    new TunnelMessageRouter( tunnelbuild, tunnel.Destination ) );

                return tunnel;
            }
        }

        internal OutboundTunnel AddTunnel( OutboundTunnel tunnel )
        {
            if ( tunnel.Established )
            {
                EstablishedOutbound[tunnel] = 1;
            }
            else
            {
                PendingOutbound[tunnel] = 1;
            }
            return tunnel;
        }

        internal InboundTunnel AddTunnel( InboundTunnel tunnel )
        {
            if ( tunnel.Established )
            {
                EstablishedInbound[tunnel] = 1;
            }
            else
            {
                PendingInbound[tunnel] = 1;
            }
            TunnelIds.Add( tunnel.ReceiveTunnelId, tunnel );
            return tunnel;
        }

        InboundTunnel AddZeroHopTunnel()
        {
            var hops = new List<HopInfo>
            {
                new HopInfo( RouterContext.Inst.MyRouterIdentity, new I2PTunnelId() )
            };
            var setup = new TunnelInfo( hops );

            var config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.Exploratory,
                setup );

            var tunnel = new ZeroHopTunnel( null, config, RouterContext.Inst.MyRouterIdentity.IdentHash );
            EstablishedInbound[tunnel] = 1;
            TunnelIds.Add( tunnel.ReceiveTunnelId, tunnel );
            return tunnel;
        }

        internal void RemoveTunnel( InboundTunnel tunnel )
        {
            TunnelIds.Remove( tunnel.ReceiveTunnelId, tunnel );
            PendingInbound.TryRemove( tunnel, out _ );
            EstablishedInbound.TryRemove( tunnel, out _ );
        }

        internal void RemoveTunnel( OutboundTunnel tunnel )
        {
            PendingOutbound.TryRemove( tunnel, out _ );
            EstablishedOutbound.TryRemove( tunnel, out _ );
        }

        internal void RemoveTunnel( Tunnel tunnel )
        {
            if ( tunnel is InboundTunnel )
            {
                RemoveTunnel( (InboundTunnel)tunnel );
            }
            else
            {
                RemoveTunnel( (OutboundTunnel)tunnel );
            }
        }

        public InboundTunnel GetInboundTunnel( bool allowexplo )
        {
            var result = GetEstablishedInboundTunnel( allowexplo );
            if ( result != null ) return result;
            if ( !allowexplo ) return null;

            return AddZeroHopTunnel();
        }

        public InboundTunnel GetEstablishedInboundTunnel( bool allowexplo )
        {
            var tunnels = EstablishedInbound
                .ToArray()
                .Where( t =>
                    t.Key.Config.Pool == TunnelConfig.TunnelPool.Client 
                    || ( allowexplo && t.Key.Config.Pool == TunnelConfig.TunnelPool.Exploratory ) )
                .Select( t => t.Key );

            return SelectTunnel<InboundTunnel>( tunnels );
        }

        public OutboundTunnel GetEstablishedOutboundTunnel( bool allowexplo )
        {
            var tunnels = EstablishedOutbound
                .ToArray()
                .Where( t => allowexplo
                    || t.Key.Config.Pool != TunnelConfig.TunnelPool.Exploratory )
                .Select( t => t.Key );

            return SelectTunnel<OutboundTunnel>( tunnels );
        }

        public IEnumerable<OutboundTunnel> GetOutboundTunnels()
        {
            return EstablishedOutbound
                .ToArray()
                .Select( t => (OutboundTunnel)t.Key );
        }

        public IEnumerable<InboundTunnel> GetInboundTunnels()
        {
            return EstablishedInbound
                .ToArray()
                .Select( t => (InboundTunnel)t.Key );
        }

        void RunIncomingMessagePump()
        {
            while ( !Terminated )
            {
                try
                {
                    if ( IncomingMessageQueue.IsEmpty ) IncommingMessageReceived.WaitOne( 500 );

                    while ( !IncomingMessageQueue.IsEmpty )
                    {
                        if ( !IncomingMessageQueue.TryDequeue( out var msg ) )
                            continue;

                        switch ( msg.MessageType )
                        {
                            case I2NPMessage.MessageTypes.VariableTunnelBuild:
                                HandleVariableTunnelBuild( msg );
                                break;

                            case I2NPMessage.MessageTypes.TunnelBuild:
                                HandleTunnelBuild( msg );
                                break;

                            case I2NPMessage.MessageTypes.TunnelGateway:
                                var tg = (TunnelGatewayMessage)msg.Message;
                                var tunnels = TunnelIds.FindTunnelFromTunnelId( tg.TunnelId );
                                if ( tunnels != null && tunnels.Any() )
                                {
                                    foreach ( var tunnel in tunnels )
                                    {
#if LOG_ALL_TUNNEL_TRANSFER
                                        Logging.Log( $"RunIncomingMessagePump: {tg}\r\n{tunnel}" );
#endif
                                        tunnel.MessageReceived(
                                            I2NPMessage.ReadHeader16( (BufRefLen)tg.GatewayMessage ).Message,
                                            msg.HeaderAndPayload.Length );
                                    }
                                }
                                else
                                {
                                    Logging.LogDebug( $"RunIncomingMessagePump: Tunnel not found for TunnelGateway. Dropped. {tg}" );
                                }
                                break;

                            case I2NPMessage.MessageTypes.TunnelData:
                                var td = (TunnelDataMessage)msg.Message;
                                tunnels = TunnelIds.FindTunnelFromTunnelId( td.TunnelId );
                                if ( tunnels != null && tunnels.Any() )
                                {
                                    foreach ( var tunnel in tunnels )
                                    {
#if LOG_ALL_TUNNEL_TRANSFER
                                        Logging.LogDebug( string.Format( "RunIncomingMessagePump: TunnelData ({0}): {1}.",
                                            td, tunnel ) );
#endif
                                        tunnel.MessageReceived( td, msg.HeaderAndPayload.Length );
                                    }
                                }
                                else
                                {
                                    Logging.LogDebug( $"RunIncomingMessagePump: Tunnel not found for TunnelData. Dropped. {td}" );
                                }
                                break;

                            default:
                                Logging.LogDebugData( () => $"TunnelProvider.RunIncomingMessagePump: Unhandled message ({msg.Message})" );
                                I2NPMessageReceived?.Invoke( msg );
                                break;
                        }
                    }
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        AutoResetEvent IncommingMessageReceived = new AutoResetEvent( false );
        public void DistributeIncomingMessage( ITransport transp, II2NPHeader msg )
        {
            IncomingMessageQueue.Enqueue( msg );
            IncommingMessageReceived.Set();
        }

        private void HandleTunnelBuild( II2NPHeader msg )
        {
            var trmsg = (TunnelBuildMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.Log( $"HandleTunnelBuild: {trmsg}" );
#endif
            HandleTunnelBuildRecords( msg, trmsg.Records );
        }

        private void HandleVariableTunnelBuild( II2NPHeader msg )
        {
            var trmsg = (VariableTunnelBuildMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.Log( $"HandleVariableTunnelBuild: {trmsg}" );
#endif
            HandleTunnelBuildRecords( msg, trmsg.Records );
        }

        private void HandleTunnelBuildRecords( II2NPHeader msg, IList<AesEGBuildRequestRecord> records )
        {
            var decrypt = new TunnelBuildRequestDecrypt( 
                records, 
                RouterContext.Inst.MyRouterIdentity.IdentHash,
                RouterContext.Inst.PrivateKey );

            if ( decrypt.ToMe() == null )
            {
                Logging.LogDebug( $"HandleTunnelBuildRecords: Failed to find a ToPeer16 record." );
                return;
            }

            if ( decrypt.Decrypted.OurIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                Logging.LogDebug( $"HandleTunnelBuildRecords: Failed to full id hash match {decrypt.Decrypted}" );
                return;
            }

            if ( decrypt.Decrypted.ToAnyone 
                    || decrypt.Decrypted.FromAnyone 
                    || decrypt.Decrypted.NextIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                TunnelBuildRequestEvents?.Invoke( msg, decrypt );
                return;
            }

            // Inbound tunnel build for me
            HandleIncomingTunnelBuildRecords( decrypt );
        }

#if DEBUG
        TimeWindowDictionary<uint, RefPair<TickCounter, int>> ReallyOldTunnelBuilds =
            new TimeWindowDictionary<uint, RefPair<TickCounter, int>>( TickSpan.Minutes( 10 ) );

#endif

        class TunnelBuildRepliesInfo
        {
            public int Count;
        }

        static ConcurrentDictionary<BuildResponseRecord.RequestResponse,TunnelBuildRepliesInfo> TunnelBuildReplies = 
                new ConcurrentDictionary<BuildResponseRecord.RequestResponse, TunnelBuildRepliesInfo>();

        static PeriodicAction LogTunnelBuildStatistics = new PeriodicAction( TickSpan.Minutes( 1 ) );

        [Conditional( "DEBUG" )]
        public static void TunnelBuildStatistics( BuildResponseRecord.RequestResponse response )
        {
            var rs = TunnelBuildReplies.GetOrAdd( response, rr => new TunnelBuildRepliesInfo() );
            ++rs.Count;

            LogTunnelBuildStatistics.Do( () =>
            {
                var items = TunnelBuildReplies
                                .OrderBy( p => (byte)p.Key )
                                .ToArray();

                var sum = items.Sum( p => p.Value.Count ) / 100.0;

                var sta = items.Select( p => $" {p.Key}: {p.Value.Count} ({p.Value.Count / sum:F1}%)" );
                var line = $"TunnelProvider: TunnelBuildStatistics:{string.Join( ',', sta )}";
                Logging.LogDebug( line );
            } );
        }

        private void HandleIncomingTunnelBuildRecords( 
                TunnelBuildRequestDecrypt decrypt )
        {
            InboundTunnel[] tunnels;
            tunnels = PendingInbound
                .Where( t => t.Key.ReceiveTunnelId == decrypt.Decrypted.ReceiveTunnel )
                .Select( t => t.Key )
                .ToArray();

            if ( tunnels.Length == 0 )
            {
#if DEBUG
                ReallyOldTunnelBuilds.ProcessItem( decrypt.Decrypted.NextTunnel, ( k, p ) =>
                {
                    Logging.LogDebug( $"Tunnel build req failed {decrypt.Decrypted.NextTunnel} age {p.Left.DeltaToNowMilliseconds / p.Right} msec / hop. Unknown tunnel id." );
                } );
#endif
                return;
            }

            var myident = RouterContext.Inst.MyRouterIdentity.IdentHash;

            var cipher = new CbcBlockCipher( new AesEngine() );

            foreach ( var tunnel in tunnels )
            {
                var setup = tunnel.Config.Info;

                var decrypted = new List<BuildResponseRecord>();

                var recordcopies = new List<AesEGBuildRequestRecord>();
                foreach ( var one in decrypt.Records )
                {
                    recordcopies.Add( new AesEGBuildRequestRecord( new BufRef( one.Data.Clone() ) ) );
                }

                for ( int i = setup.Hops.Count - 1; i >= 0; --i )
                {
                    var hop = setup.Hops[i];
                    var proc = hop.ReplyProcessing;
                    cipher.Init( false, proc.ReplyKey.Key.ToParametersWithIV( proc.ReplyIV ) );

                    var rec = recordcopies[proc.BuildRequestIndex];
                    if ( myident.Hash16 == rec.ToPeer16 ) continue;

                    for ( int j = 0; j <= i; ++j )
                    {
                        cipher.Reset();
                        recordcopies[setup.Hops[j].ReplyProcessing.BuildRequestIndex].Process( cipher );
                    }

                    var newrec = new BuildResponseRecord( new BufRefLen( rec.Data ) );

                    decrypted.Add( newrec );

                    TunnelBuildStatistics( newrec.Reply );

                    if ( newrec.Reply == BuildResponseRecord.RequestResponse.Accept )
                    {
                        Logging.LogDebug( $"HandleTunnelBuildRecords: {tunnel} {tunnel.TunnelDebugTrace} " +
                            $"member: {hop.Peer.IdentHash.Id32Short}. Hop {i}. Reply: {newrec.Reply}" );

                        NetDb.Inst.Statistics.SuccessfulTunnelMember( hop.Peer.IdentHash );
                    }
                    else
                    {
                        Logging.LogDebug( $"HandleTunnelBuildRecords: {tunnel} {tunnel.TunnelDebugTrace} " +
                            $"member: {hop.Peer.IdentHash.Id32Short}. Hop {i}. Reply: {newrec.Reply}" );

                        NetDb.Inst.Statistics.DeclinedTunnelMember( hop.Peer.IdentHash );
                    }
                }

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"HandleIncomingTunnelBuildRecords: {tunnel.Destination.Id32Short} " +
                    $"My inbound tunnel {tunnel.TunnelDebugTrace} request for tunnel id {tunnel.ReceiveTunnelId}" );
#endif

                if ( decrypted.All( r => r.Reply == BuildResponseRecord.RequestResponse.Accept ) )
                {
                    InboundTunnelEstablished( tunnel );
                }
                else
                {
                    Logging.LogDebug( $"HandleIncomingTunnelBuildRecords: Tunnel {tunnel.TunnelDebugTrace} build rejected." );

                    tunnel.Owner?.TunnelBuildFailed( tunnel, false );
                    tunnel.Shutdown();
                }
            }
        }

        internal void HandleTunnelBuildReply( VariableTunnelBuildReplyMessage msg )
        {
            var matching = PendingOutbound
                .Where( po => po.Key.TunnelBuildReplyMessageId == msg.MessageId )
                .Select( po => po.Key );

            foreach ( var obtunnel in matching )
            {
                HandleReceivedTunnelBuildReply( obtunnel, msg );

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( () => $"HandleTunnelBuildReply: MsgId match {msg.MessageId:X8}." );
#endif
            }

#if DEBUG
            if ( !matching.Any() )
            {
                ReallyOldTunnelBuilds.ProcessItem( msg.MessageId, ( k, p ) =>
                    Logging.LogDebug( $"Tunnel build req failed {msg.MessageId} age {p.Left.DeltaToNowMilliseconds / p.Right} msec / hop. MessageId unknown." )
                );
            }
#endif
        }

        private bool HandleReceivedTunnelBuildReply( OutboundTunnel obtunnel, VariableTunnelBuildReplyMessage msg )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            var hops = obtunnel.Config.Info.Hops;

            for ( int i = hops.Count - 1; i >= 0; --i )
            {
                var proc = hops[i].ReplyProcessing;
                cipher.Init( false, proc.ReplyKey.Key.ToParametersWithIV( proc.ReplyIV ) );

                for ( int j = 0; j <= i; ++j )
                {
                    cipher.Reset();
                    var pl = msg.ResponseRecords[hops[j].ReplyProcessing.BuildRequestIndex].Payload;
                    cipher.ProcessBytes( pl );
                }
            }

            bool ok = true;
            for ( int i = 0; i < hops.Count; ++i )
            {
                var hop = hops[i];

                var ix = hop.ReplyProcessing.BuildRequestIndex;
                var onerecord = msg.ResponseRecords[ix];

                var okhash = onerecord.CheckHash();
                if ( !okhash )
                {
                    Logging.LogDebug( $"OutboundTunnel {obtunnel.TunnelDebugTrace}: Outbound tunnel build reply, hash check failed from {hop.Peer.IdentHash.Id32Short}" );
                    NetDb.Inst.Statistics.DestinationInformationFaulty( hop.Peer.IdentHash );
                }

                TunnelBuildStatistics( onerecord.Reply );

                var accept = onerecord.Reply == BuildResponseRecord.RequestResponse.Accept;
                if ( accept )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelMember( hop.Peer.IdentHash );
                }
                else
                {
                    NetDb.Inst.Statistics.DeclinedTunnelMember( hop.Peer.IdentHash );
                }

                ok &= accept && okhash;
                Logging.LogDebug( $"HandleReceivedTunnelBuild: {this}: [{ix}] " +
                    $"from {hop.Peer.IdentHash.Id32Short}. {hops.Count} hops, " +
                    $"Reply: {onerecord.Reply}" );
            }

            if ( ok )
            {
                TunnelProvider.Inst.OutboundTunnelEstablished( obtunnel );
                foreach ( var one in hops )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
            }
            else
            {
                obtunnel.Owner?.TunnelBuildFailed( obtunnel, false );

                foreach ( var one in hops )
                {
                    NetDb.Inst.Statistics.DeclinedTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
                obtunnel.Shutdown();
            }

            return ok;
        }

        private void OutboundTunnelEstablished( OutboundTunnel tunnel )
        {
            tunnel.EstablishedTime.SetNow();
            tunnel.Established = true;

            if ( tunnel.Pool == TunnelConfig.TunnelPool.Client || tunnel.Pool == TunnelConfig.TunnelPool.Exploratory )
            {
                var members = tunnel.TunnelMembers.ToArray();
                if ( members != null )
                {
                    var delta = ( tunnel.EstablishedTime - tunnel.CreationTime ).ToMilliseconds;
                    var hops = members.Length + tunnel.ReplyTunnelHops;
                    var deltaperhop = delta / hops;
                    tunnel.Metrics.BuildTimePerHop = TickSpan.Milliseconds( deltaperhop );
                    try
                    {
                        foreach ( var member in members ) NetDb.Inst.Statistics.TunnelBuildTimeMsPerHop( member.IdentHash, deltaperhop );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }

#if RUN_TUNNEL_TESTS
                if ( !tunnel?.Terminated ?? false ) TunnelTester.Inst.Test( tunnel );
#endif
            }

            PendingOutbound.TryRemove( tunnel, out _ );
            EstablishedOutbound[tunnel] = 1;

            tunnel.Owner?.TunnelEstablished( tunnel );
        }

        private void InboundTunnelEstablished( InboundTunnel tunnel )
        {
            tunnel.EstablishedTime.SetNow();
            tunnel.Established = true;

            if ( tunnel.Pool == TunnelConfig.TunnelPool.Client || tunnel.Pool == TunnelConfig.TunnelPool.Exploratory )
            {
                var members = tunnel.TunnelMembers.ToArray();
                if ( members != null )
                {
                    var delta = ( tunnel.EstablishedTime - tunnel.CreationTime ).ToMilliseconds;
                    var hops = members.Length + tunnel.OutTunnelHops - 1;
                    var deltaperhop = delta / hops;
                    tunnel.Metrics.BuildTimePerHop = TickSpan.Milliseconds( deltaperhop );
                    try
                    {
                        foreach ( var member in members ) NetDb.Inst.Statistics.TunnelBuildTimeMsPerHop( member.IdentHash, deltaperhop );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }

#if RUN_TUNNEL_TESTS
                if ( !tunnel?.Terminated ?? false ) TunnelTester.Inst.Test( tunnel );
#endif
            }

            PendingInbound.TryRemove( tunnel, out _ );
            EstablishedInbound[tunnel] = 1;

            tunnel.Owner?.TunnelEstablished( tunnel );
        }

        internal void TunnelTestFailed( Tunnel tunnel )
        {
            tunnel.Owner?.TunnelFailed( tunnel );

            RemoveTunnel( tunnel );
            tunnel.Shutdown();
        }

        public static T SelectTunnel<T>( IEnumerable<Tunnel> tunnels ) where T : Tunnel
        {
            if ( tunnels.Any() )
            {
                var result = (T)tunnels.RandomWeighted(
                    GenerateTunnelWeight, TunnelSelectionElitism );

#if LOG_TUNNEL_SELECTION
                var available = string.Join( ',', tunnels.Select( t => t.CreationTime.DeltaToNow.ToString( "MS" ) ) );
                Logging.LogDebug( $"TunnelProvider: SelectTunnel {result}, ({available})" );
#endif
                return result;
            }

            return null;
        }

        public static double GenerateTunnelWeight( Tunnel t )
        {
            var penalty = Tunnel.ExpectedTunnelBuildTimePerHop.ToMilliseconds * 2.0;

            var result = t.Metrics.MinLatencyMeasured?.ToMilliseconds ?? penalty;
            result += t.Metrics.BuildTimePerHop?.ToMilliseconds ?? penalty;
            result += t.CreationTime.DeltaToNowSeconds * 10;
            if ( t.NeedsRecreation ) result += penalty;
            if ( t.Pool == TunnelConfig.TunnelPool.Exploratory ) result += penalty / 2.0;
            if ( t.Expired ) result += penalty;
            if ( t.Terminated ) result += 10 * penalty;
            if ( !t.Metrics.PassedTunnelTest ) result += penalty;
            if ( t is ZeroHopTunnel ) result += 10 * penalty;

            return -result;
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
