#define RUN_TUNNEL_TESTS

using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.Threading;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Transport;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace I2PCore.Tunnel
{
    public class TunnelProvider
    {
        public static TunnelProvider Inst { get; protected set; }

        public event Action<II2NPHeader, TunnelBuildRequestDecrypt>
            TunnelBuildRequestEvents;

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

        DestinationSessions RouterSession;

        internal bool ClientTunnelsStatusOk { get; set; }
        internal bool AcceptTransitTunnels { get => ClientTunnelsStatusOk; }

        TunnelProvider()
        {
            RouterSession = new DestinationSessions( 
                new OutboundTunnelSelector( RouterDestTunnelSelector ),
                new InboundTunnelSelector( RouterReplyTunnelSelector ) );

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

        void RouterDestTunnelSelector( I2PLeaseSet ls, II2NPHeader16 header, GarlicCreationInfo info )
        {
            var outtunnel = GetEstablishedOutboundTunnel( false );
            if ( outtunnel == null ) return;

            outtunnel.Send( new TunnelMessageRouter( header, info.Destination ) );
        }

        InboundTunnel RouterReplyTunnelSelector()
        {
            return GetInboundTunnel( false );
        }

        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 10 ) );
        PeriodicAction TunnelBandwidthLog = new PeriodicAction( TickSpan.Minutes( 8 ) );

        PeriodicAction CheckTunnelTimeouts = new PeriodicAction( Tunnel.MeassuredTunnelBuildTimePerHop );
        PeriodicAction RunDestinationEncyption = new PeriodicAction( TickSpan.Seconds( 3 ) );

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
                            LogTunnelBandwidth( EstablishedInbound.Keys.ToArray() );
                            LogTunnelBandwidth( EstablishedOutbound.Keys.ToArray() );
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

                        RunDestinationEncyption.Do( RouterSession.Run );

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
            foreach ( var tunnel in tunnels )
            {
                if ( tunnel.Config.Direction == TunnelConfig.TunnelDirection.Inbound && tunnel.TunnelMembers.Any() )
                {
                    foreach ( var peer in tunnel.TunnelMembers.Select( id => id.IdentHash ).ToArray() )
                    {
                        NetDb.Inst.Statistics.MaxBandwidth( peer, tunnel.Bandwidth.ReceiveBandwidth );
                    }
                }

                Logging.LogInformation( $"Tunnel bandwidth {tunnel,-35} {tunnel.Bandwidth}" );
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
                one.Owner?.TunnelBuildTimeout( one );

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
                    st.Append( $"{onepool.Key} " );
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
                        tunnel.Owner?.TunnelBuildTimeout( tunnel );
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
                Logging.LogDebug( $"TunnelProvider: Outbound tunnel {config.Pool} " +
                    $"({config.Info.Hops.Count}) {tunnel.TunnelDebugTrace} " +
                    $"created to {tunnel.Destination.Id32Short}, build id: {tunnel.TunnelBuildReplyMessageId}." );

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

                Logging.LogDebug( $"TunnelProvider: Inbound tunnel {config.Pool} " +
                    $"({config.Info.Hops.Count}) {tunnel.TunnelDebugTrace} " +
                    $"created to {tunnel.Destination.Id32Short}." );
#if DEBUG
                ReallyOldTunnelBuilds.Set( tunnel.TunnelBuildReplyMessageId,
                    new RefPair<TickCounter, int>( TickCounter.Now, outtunnel.Config.Info.Hops.Count + config.Info.Hops.Count ) );
#endif

                var tunnelbuild = tunnel.CreateBuildRequest();

                outtunnel.Send(
                    new TunnelMessageRouter( tunnelbuild.Header16, tunnel.Destination ) );

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

            var tunnel = new ZeroHopTunnel( null, config );
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

            return AddZeroHopTunnel();
        }

        double GenerateTunnelWeight( Tunnel t )
        {
            var penalty = Tunnel.MeassuredTunnelBuildTimePerHop.ToMilliseconds * 2.0;

            var result = t?.MinLatencyMeasured?.ToMilliseconds ?? penalty;
            result += t.CreationTime.DeltaToNowMilliseconds;
            if ( t.NeedsRecreation ) result += penalty;
            if ( t.Pool == TunnelConfig.TunnelPool.Exploratory ) result += penalty / 2.0;
            if ( !t.Active ) result += penalty;
            if ( t.Expired ) result += penalty;
            if ( t is ZeroHopTunnel ) result += 10 * penalty;

            return result;
        }

        public InboundTunnel GetEstablishedInboundTunnel( bool allowexplo )
        {
            var tunnels = EstablishedInbound
                .ToArray()
                .Where( t =>
                    t.Key.Config.Pool == TunnelConfig.TunnelPool.Client 
                    || ( allowexplo && t.Key.Config.Pool == TunnelConfig.TunnelPool.Exploratory ) )
                .Select( t => t.Key );

            if ( tunnels.Any() )
            {
                var result = (InboundTunnel)tunnels.RandomWeighted(
                    GenerateTunnelWeight, true, TunnelSelectionElitism );

                return result;
            }

            return null;
        }

        public OutboundTunnel GetEstablishedOutboundTunnel( bool allowexplo )
        {
            var tunnels = EstablishedOutbound
                .ToArray()
                .Where( t => allowexplo
                    || t.Key.Config.Pool != TunnelConfig.TunnelPool.Exploratory )
                .Select( t => t.Key );

            if ( tunnels.Any() )
            {
                return (OutboundTunnel)tunnels.RandomWeighted(
                    GenerateTunnelWeight, true, TunnelSelectionElitism );
            }

            return null;
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

        static object DeliveryStatusReceivedLock = new object();
        public static event Action<DeliveryStatusMessage> DeliveryStatusReceived;

        void RunIncomingMessagePump()
        {
            while ( !Terminated )
            {
                try
                {
                    if ( IncomingMessageQueue.Count == 0 ) IncommingMessageReceived.WaitOne( 500 );

                    while ( !IncomingMessageQueue.IsEmpty )
                    {
                        if ( !IncomingMessageQueue.TryDequeue( out var msg ) )
                            continue;

                        switch ( msg.MessageType )
                        {
                            case I2NPMessage.MessageTypes.DatabaseStore:
                                var ds = (DatabaseStoreMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.Log( $"RunIncomingMessagePump: DatabaseStore : {ds.Key.Id32Short}" );
#endif
                                HandleDatabaseStore( ds );
                                break;

                            case I2NPMessage.MessageTypes.DatabaseSearchReply:
                                var dsr = (DatabaseSearchReplyMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.Log( $"TunnelProvider.RunIncomingMessagePump: DatabaseSearchReply: {dsr}" );
#endif
                                NetDb.Inst.AddDatabaseSearchReply( dsr );
                                break;

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
                                            I2NPMessage.ReadHeader16( (BufRefLen)tg.GatewayMessage ),
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
                                        tunnel.MessageReceived( msg );
                                    }
                                }
                                else
                                {
                                    Logging.LogDebug( "RunIncomingMessagePump: Tunnel not found for TunnelData. Dropped. " + td.ToString() );
                                }
                                break;

                            case I2NPMessage.MessageTypes.DeliveryStatus:
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( "TunnelProvider.RunIncomingMessagePump: DeliveryStatus: " + msg.Message.ToString() );
#endif

                                ThreadPool.QueueUserWorkItem( cb =>
                                {
                                    lock ( DeliveryStatusReceivedLock )
                                    {
                                        var dsmsg = (DeliveryStatusMessage)msg.Message;
                                        DeliveryStatusReceived?.Invoke( dsmsg );
                                    }
                                } );
                                break;

                            default:
                                Logging.LogDebug( () => "TunnelProvider.RunIncomingMessagePump: Unhandled message (" + msg.Message.ToString() + ")" );
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

        internal static void HandleDatabaseStore( DatabaseStoreMessage ds )
        {
            if ( ds.RouterInfo == null && ds.LeaseSet == null ) throw new ArgumentException( "DatabaseStore without Router or Lease info!" );

            if ( ds.RouterInfo != null )
            {
#if LOG_ALL_TUNNEL_TRANSFER
                //Logging.Log( "HandleDatabaseStore: DatabaseStore RouterInfo" + ds.ToString() );
#endif
                NetDb.Inst.AddRouterInfo( ds.RouterInfo );
            }
            else
            {
#if LOG_ALL_TUNNEL_TRANSFER
                //Logging.Log( "HandleDatabaseStore: DatabaseStore LeaseSet" + ds.ToString() );
#endif
                NetDb.Inst.AddLeaseSet( ds.LeaseSet );
            }

            if ( ds.ReplyToken != 0 )
            {
                if ( ds.ReplyTunnelId != 0 )
                {
                    var outtunnel = TunnelProvider.Inst.GetEstablishedOutboundTunnel( true );
                    if ( outtunnel != null )
                    {
                        outtunnel.Send( new TunnelMessageRouter(
                            ( new TunnelGatewayMessage(
                                ( new DeliveryStatusMessage( ds.ReplyToken ) ).Header16,
                                ds.ReplyTunnelId ) ).Header16,
                            ds.ReplyGateway ) );
                    }
                }
                else
                {
                    TransportProvider.Send( ds.ReplyGateway,
                        new DeliveryStatusMessage( ds.ReplyToken ) );
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

                    tunnel.Shutdown();
                }
            }
        }

        internal void HandleTunnelBuildReply( II2NPHeader16 header )
        {
            var matching = PendingOutbound
                .Where( po => po.Key.TunnelBuildReplyMessageId == header.MessageId )
                .Select( po => po.Key );

            foreach ( var match in matching )
            {
                match.MessageReceived( header );

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( () => $"HandleTunnelBuildReply: MsgId match {header.MessageId:X8}." );
#endif
            }

#if DEBUG
            if ( !matching.Any() )
            {
                ReallyOldTunnelBuilds.ProcessItem( header.MessageId, ( k, p ) =>
                    Logging.LogDebug( $"Tunnel build req failed {header.MessageId} age {p.Left.DeltaToNowMilliseconds / p.Right} msec / hop. MessageId unknown." )
                );
            }
#endif
        }

        // TODO: Review
        internal void OutboundTunnelEstablished( OutboundTunnel tunnel )
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
                    try
                    {
                        foreach ( var member in members ) NetDb.Inst.Statistics.TunnelBuildTimeMsPerHop( member.IdentHash, delta / hops );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
            }

            PendingOutbound.TryRemove( tunnel, out _ );
            EstablishedOutbound[tunnel] = 1;

            tunnel.Owner?.TunnelEstablished( tunnel );
        }

        // TODO: Review
        internal void InboundTunnelEstablished( InboundTunnel tunnel )
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
                    try
                    {
                        foreach ( var member in members ) NetDb.Inst.Statistics.TunnelBuildTimeMsPerHop( member.IdentHash, delta / hops );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
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

        internal void LocalLeaseSetChanged( I2PLeaseSet leaseset )
        {
            var list = NetDb.Inst.GetClosestFloodfill( leaseset.Destination.IdentHash, 15, null, false );
            if ( list == null || !list.Any()) list = NetDb.Inst.GetRandomFloodfillRouter( true, 10 );
            list = list.Shuffle().Take( 4 );

            if ( DateTime.UtcNow.Hour >= 23 )
            {
                var nextlist = NetDb.Inst.GetClosestFloodfill( leaseset.Destination.IdentHash, 15, null, true ).Shuffle().Take( 4 );
                if ( nextlist != null ) list = list.Concat( nextlist );
            }

            foreach ( var ff in list )
            {
                try
                {
                    if ( ff == null )
                    {
                        Logging.Log( "LocalLeaseSetChanged failed to find a floodfill router." );
                        return;
                    }

                    var sendtunnel = GetEstablishedOutboundTunnel( false );
                    var replytunnel = GetEstablishedInboundTunnel( false );

                    if ( sendtunnel == null || replytunnel == null )
                    {
                        Logging.LogDebug( "LocalLeaseSetChanged, no available tunnels." );
                        continue;
                    }

                    Logging.Log( "LocalLeaseSetChanged (" + leaseset.Destination.IdentHash.Id32Short + "): " + ff.Id32Short );

                    // A router publishes a local LeaseSet by sending a I2NP DatabaseStoreMessage with a 
                    // nonzero Reply Token over an outbound client tunnel for that Destination. 
                    // https://geti2p.net/en/docs/how/network-database
                    var ds = new DatabaseStoreMessage(
                        leaseset,
                        BufUtils.RandomUint() | 0x01, replytunnel.Destination, replytunnel.ReceiveTunnelId
                        );

                    // As explained on the network database page, local LeaseSets are sent to floodfill 
                    // routers in a Database Store Message wrapped in a Garlic Message so it is not 
                    // visible to the tunnel's outbound gateway.

                    /*
                    var garlic = Garlic.Create( new GarlicCloveDeliveryLocal( ds ) );
                    var eggarlicmsg = DestinationSessions.GenerateGarlicMessage( ff.Identity, garlic );
                     */

                    // RouterSession.Send( ff.Identity, true, new GarlicCloveDeliveryLocal( ds ) );

                    /*
                    var gmsginfo = RouterSession.CreateMessage( ff.Identity, new GarlicCloveDeliveryLocal( ds ) );
                    sendtunnel.Send( new TunnelMessageRouter( new GarlicMessage( gmsginfo.Garlic ).Header16, ff.Identity.IdentHash ) );
                    TransportProvider.Send( ff.Identity.IdentHash, ds );
                     */
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        public void SendEncrypted( I2PKeysAndCert dest, bool ack, I2NPMessage data )
        {
            RouterSession.Send( dest, ack, new GarlicCloveDeliveryLocal( data ) );
        }
    }
}
