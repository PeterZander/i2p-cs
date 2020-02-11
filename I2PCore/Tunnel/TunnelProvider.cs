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

namespace I2PCore.Tunnel
{
    public class TunnelProvider
    {
        private const double TunnelSelectionElitism = 5.0;

        List<Tunnel> PendingOutbound = new List<Tunnel>();
        List<Tunnel> PendingInbound = new List<Tunnel>();

        List<Tunnel> ClientsOutbound = new List<Tunnel>();
        List<Tunnel> ClientsInbound = new List<Tunnel>();

        List<Tunnel> ExploratoryPendingOutbound = new List<Tunnel>();
        List<Tunnel> ExploratoryPendingInbound = new List<Tunnel>();

        List<Tunnel> ExploratoryOutbound = new List<Tunnel>();
        List<Tunnel> ExploratoryInbound = new List<Tunnel>();

        List<Tunnel> ExternalTunnels = new List<Tunnel>();

        Dictionary<uint, List<Tunnel>> TunnelIds = new Dictionary<uint, List<Tunnel>>();

        internal ClientTunnelProvider ClientsMgr;
        internal ExplorationTunnelProvider ExplorationMgr;
        internal PassthroughTunnelProvider PassthroughMgr;

        public int PendingOutboundTunnelCount { get { return PendingOutbound.Count; } }
        public int PendingInboundTunnelCount { get { return PendingInbound.Count; } }

        public int OutboundTunnelCount { get { return ClientsOutbound.Count; } }
        public int InboundTunnelCount { get { return ClientsInbound.Count; } }

        public int ExternalTunnelCount { get { return ClientsInbound.Count; } }

        public int ExploratoryPendingOutboundTunnelCount { get { return ExploratoryPendingOutbound.Count; } }
        public int ExploratoryPendingInboundTunnelCount { get { return ExploratoryPendingInbound.Count; } }

        public int ExploratoryOutboundTunnelCount { get { return ExploratoryOutbound.Count; } }
        public int ExploratoryInboundTunnelCount { get { return ExploratoryInbound.Count; } }

        public int ExploratoryActiveOutboundTunnelCount { 
            get { lock( ExploratoryOutbound ) return ExploratoryOutbound.Count( t => !t.Active ); } }
        public int ExploratoryActiveInboundTunnelCount { 
            get { lock( ExploratoryInbound ) return ExploratoryInbound.Count( t => !t.Active ); } }

        protected static Thread Worker;

        public static TunnelProvider Inst { get; protected set; }

        LinkedList<II2NPHeader> IncomingMessageQueue = new LinkedList<II2NPHeader>();
        protected static Thread IncomingMessagePump;

        DestinationSessions RouterSession;

        TunnelProvider()
        {
            ClientsMgr = new ClientTunnelProvider( this );
            ExplorationMgr = new ExplorationTunnelProvider( this );
            PassthroughMgr = new PassthroughTunnelProvider( this );

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

        PeriodicAction CheckTunnelTimeouts = new PeriodicAction( TickSpan.Seconds( Tunnel.MeassuredTunnelBuildTimePerHopSeconds ) );
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
                        ClientsMgr.Execute();
                        ExplorationMgr.Execute();
                        PassthroughMgr.Execute();

                        TunnelBandwidthLog.Do( () => ThreadPool.QueueUserWorkItem( cb =>
                        {
                            LogTunnelBandwidth( ExploratoryOutbound );
                            LogTunnelBandwidth( ClientsOutbound );
                            LogTunnelBandwidth( ExploratoryInbound );
                            LogTunnelBandwidth( ClientsInbound );
                            LogTunnelBandwidth( ExternalTunnels );
                        } ) );

                        var outt = ClientsOutbound.Count + ExploratoryOutbound.Count + PendingOutbound.Count;
                        var estt = EstablishedTunnels;
                        QueueStatusLog.Do( () =>
                            {
                                Logging.LogInformation(
                                    $"Established client tunnels in: {ClientsInbound.Count,2} ( {PendingInbound.Count,2} ), " +
                                    $"out: {ClientsOutbound.Count,2} ( {PendingOutbound.Count,2} )" );

                                Logging.LogInformation(
                                    $"Established explo  tunnels in: {ExploratoryInboundTunnelCount,2} ( {ExploratoryPendingInboundTunnelCount,2} ), " +
                                    $"out: {ExploratoryOutboundTunnelCount,2} ( {ExploratoryPendingOutboundTunnelCount,2} )" );

                                Logging.LogInformation(
                                    $"Established passth tunnels   : {ExternalTunnels.Count,2}, Firewalled: {RouterContext.Inst.IsFirewalled}" );

                                Logging.LogDebug( () => string.Format(
                                    "Unresolvable routers: {0}. Unresolved routers: {1}. IP addresses with execptions: {2}. SSU blocked IPs: {3}.",
                                    TransportProvider.Inst.CurrentlyUnresolvableRoutersCount,
                                    TransportProvider.Inst.CurrentlyUnknownRoutersCount,
                                    TransportProvider.Inst.AddressesWithExceptionsCount,
                                    TransportProvider.Inst.SsuHostBlockedIPCount
                                    ) );
                            } );

                        CheckTunnelTimeouts.Do( CheckForTunnelBuildTimeout );

                        ExecuteQueue( PendingOutbound );
                        ExecuteQueue( PendingInbound );
                        ExecuteQueue( ExploratoryPendingOutbound );
                        ExecuteQueue( ExploratoryPendingInbound );
                        ExecuteQueue( ExploratoryOutbound );
                        ExecuteQueue( ClientsOutbound );
                        ExecuteQueue( ExploratoryInbound );
                        ExecuteQueue( ClientsInbound );
                        ExecuteQueue( ExternalTunnels );

                        RunDestinationEncyption.Do( RouterSession.Run );

                        Thread.Sleep( 500 ); // Give data a chance to batch up
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
                            Logging.Log( "TransportProvider: Communication exception " + ex.GetType().ToString() );
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

        private void LogTunnelBandwidth( List<Tunnel> tunnels )
        {
            Tunnel[] tar;

            lock ( tunnels )
            {
                tar = tunnels.ToArray();
            }

            foreach ( var tunnel in tar )
            {
                if ( tunnel.Config.Direction == TunnelConfig.TunnelDirection.Inbound && tunnel.TunnelMembers != null )
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
                CheckForTunnelBuildTimeout( PendingOutbound );
            }

            if ( PendingInbound.Count > 0 )
            {
                CheckForTunnelBuildTimeout( PendingInbound );
            }

            if ( ExploratoryPendingOutbound.Count > 0 )
            {
                CheckForTunnelBuildTimeout( ExploratoryPendingOutbound );
            }

            if ( ExploratoryPendingInbound.Count > 0 )
            {
                CheckForTunnelBuildTimeout( ExploratoryPendingInbound );
            }

            CheckForTunnelReplacementTimeout();
        }

        private void CheckForTunnelBuildTimeout( List<Tunnel> pool )
        {
            lock ( pool )
            {
                var timeout = pool.Where( t => 
                    t.CreationTime.DeltaToNowSeconds > t.TunnelEstablishmentTimeoutSeconds )
                        .ToArray();

#if DEBUG
                var removedtunnels = new List<Tunnel>();
#endif
                foreach ( var one in timeout )
                {
#if DEBUG
                    removedtunnels.Add( one );
#endif
                    foreach ( var dest in one.TunnelMembers )
                    {
                        NetDb.Inst.Statistics.TunnelBuildTimeout( dest.IdentHash );
                    }

                    RemoveTunnel( one );
                    one.Shutdown();

                    switch ( one.Pool )
                    {
                        case TunnelConfig.TunnelPool.Exploratory:
                            ExplorationMgr.TunnelBuildTimeout( one );
                            break;

                        case TunnelConfig.TunnelPool.Client:
                            ClientsMgr.TunnelBuildTimeout( one );
                            break;

                        case TunnelConfig.TunnelPool.External:
                            PassthroughMgr.TunnelBuildTimeout( one );
                            break;

                        default: throw new NotImplementedException();
                    }
                }
#if DEBUG
                if ( removedtunnels.Any() )
                {
                    var st = new StringBuilder();
                    var pools = removedtunnels.GroupBy( t => t.Pool );
                    foreach ( var onepool in pools )
                    {
                        st.Append( $"{onepool.First()} " );
                        foreach ( var one in pool )
                        {
                            st.Append( $"{one.TunnelDebugTrace} " );
                        }
                    }

                    Logging.LogDebug( $"TunnelProvider: Removing {st} due to establishment timeout." );
                }
#endif
            }
        }

        private void CheckForTunnelReplacementTimeout()
        {
            CheckForTunnelReplacementTimeout( ClientsOutbound );
            CheckForTunnelReplacementTimeout( ClientsInbound );
        }

        private void CheckForTunnelReplacementTimeout( List<Tunnel> pool )
        {
            IEnumerable<Tunnel> timeoutlist;

            lock ( pool )
            {
                timeoutlist = pool.Where( t => t.NeedsRecreation ).ToArray();
            }

            foreach ( var one in timeoutlist )
            {
                /*
                Logging.LogDebug( "TunnelProvider: CheckForTunnelReplacementTimeout: " + one.Pool.ToString() + 
                    " tunnel " + one.TunnelDebugTrace + " needs to be replaced." );
                 */
                ClientsMgr.TunnelReplacementNeeded( one );
            }
        }

        private void ExecuteQueue( List<Tunnel> queue )
        {
            IEnumerable<Tunnel> q;
            lock ( queue )
            {
                q = queue.ToArray();
            }

            var failed = new List<Tunnel>();

            foreach ( var tunnel in q )
            {
                try
                {
                    var ok = tunnel.Exectue();
                    if ( !ok )
                    {
                        failed.Add( tunnel );
                    }
                    else
                    {
                        if ( tunnel.Expired ) 
                        {
                            // Normal timeout
                            Logging.LogDebug( string.Format( "TunnelProvider: ExecuteQueue removing tunnel {0} to {1}. Lifetime. Created: {2}.",
                                tunnel.TunnelDebugTrace, tunnel.Destination.Id32Short, tunnel.EstablishedTime ) );

                            failed.Add( tunnel );
                            tunnel.Shutdown();
                        }
                    }
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug( string.Format( "TunnelProvider {0}: Exception [{1}] '{2}' in tunnel to {3}. {4}",
                        tunnel.TunnelDebugTrace, ex.GetType(), ex.Message, tunnel.Destination.Id32Short, tunnel ) );
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( ex );
#endif
                    failed.Add( tunnel );
                }
            }

            lock ( queue )
            {
                foreach ( var one in failed )
                {
                    Logging.LogDebug( string.Format( "TunnelProvider {0}: ExecuteQueue removing failed tunnel to {1} pool {2}.",
                        one.TunnelDebugTrace, one.Destination.Id32Short, one.Config.Pool ) );

                    switch ( one.Pool )
                    {
                        case TunnelConfig.TunnelPool.Exploratory:
                            ExplorationMgr.TunnelTimeout( one );
                            break;

                        case TunnelConfig.TunnelPool.Client:
                            ClientsMgr.TunnelTimeout( one );
                            break;

                        case TunnelConfig.TunnelPool.External:
                            PassthroughMgr.TunnelTimeout( one );
                            break;

                        default: throw new NotImplementedException();
                    }

                    queue.Remove( one );
                    RemoveTunnel( one );
                }
            }
        }

        public Tunnel CreateTunnel( TunnelConfig config )
        {
            if ( config.Direction == TunnelConfig.TunnelDirection.Outbound )
            {
                var replytunnel = GetInboundTunnel( true );
                var tunnel = new OutboundTunnel( config, replytunnel.Config.Info.Hops.Count );

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
                if ( outtunnel == null ) return null;

                var tunnel = new InboundTunnel( config, outtunnel.Config.Info.Hops.Count );

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
            if ( tunnel.Config.Pool == TunnelConfig.TunnelPool.Exploratory )
            {
                lock ( ExploratoryPendingOutbound )
                {
                    ExploratoryPendingOutbound.Add( tunnel );
                }
            }
            else
            {
                lock ( PendingOutbound )
                {
                    PendingOutbound.Add( tunnel );
                }
            }
            return tunnel;
        }

        internal InboundTunnel AddTunnel( InboundTunnel tunnel )
        {
            if ( tunnel.Config.Pool == TunnelConfig.TunnelPool.Exploratory )
            {
                lock ( ExploratoryPendingInbound )
                {
                    ExploratoryPendingInbound.Add( tunnel );
                }
            }
            else
            {
                lock ( PendingInbound )
                {
                    PendingInbound.Add( tunnel );
                }
            }

            AddTunnelId( tunnel, tunnel.ReceiveTunnelId );
            return tunnel;
        }

        internal void RemoveTunnel( Tunnel tunnel )
        {
            if ( tunnel is InboundTunnel )
            {
                RemoveTunnelFromPool( tunnel, PendingInbound );
                RemoveTunnelFromPool( tunnel, ExploratoryPendingInbound );
                RemoveTunnelFromPool( tunnel, ClientsInbound );
                RemoveTunnelFromPool( tunnel, ExploratoryInbound );
                RemoveTunnelId( tunnel, tunnel.ReceiveTunnelId );
            }
            else
                if ( tunnel is OutboundTunnel )
                {
                    RemoveTunnelFromPool( tunnel, PendingOutbound );
                    RemoveTunnelFromPool( tunnel, ExploratoryPendingOutbound );
                    RemoveTunnelFromPool( tunnel, ClientsOutbound );
                    RemoveTunnelFromPool( tunnel, ExploratoryOutbound );
                }
                else
                {
                    RemoveTunnelFromPool( tunnel, ExternalTunnels );
                    RemoveTunnelId( tunnel, tunnel.ReceiveTunnelId );
                }
        }

        private static void RemoveTunnelFromPool( Tunnel tunnel, List<Tunnel> pool )
        {
            lock ( pool )
            {
                pool.RemoveAll( t => t == tunnel );
            }
        }

        internal void AddExternalTunnel( Tunnel tunnel )
        {
            lock ( ExternalTunnels )
            {
                ExternalTunnels.Add( tunnel );
            }
            AddTunnelId( tunnel, tunnel.ReceiveTunnelId );
        }

        InboundTunnel AddZeroHopTunnel( InboundTunnel tunnel )
        {
            lock ( ExploratoryInbound )
            {
                ExploratoryInbound.Add( tunnel );
            }
            AddTunnelId( tunnel, tunnel.ReceiveTunnelId );
            return tunnel;
        }

        public int EstablishedTunnels
        {
            get
            {
                int result = 0;
                lock ( ExploratoryOutbound ) result += ExploratoryOutbound.Count;
                lock ( ClientsOutbound ) result += ClientsOutbound.Count;
                lock ( ExploratoryInbound ) result += ExploratoryInbound.Count;
                lock ( ClientsInbound ) result += ClientsInbound.Count;
                return result;
            }
        }

        public InboundTunnel GetInboundTunnel( bool allowexplo )
        {
            var result = GetEstablishedInboundTunnel( allowexplo );
            if ( result != null ) return result;

            return AddZeroHopTunnel( new InboundTunnel( null, 0 ) );
        }

        double GenerateTunnelWeight( Tunnel t )
        {
            var penalty = 2.0 * Tunnel.MeassuredTunnelBuildTimePerHopSeconds * 1000.0;

            var result = t?.MinLatencyMeasured?.ToMilliseconds ?? penalty;
            result += t.CreationTime.DeltaToNowMilliseconds;
            if ( t.NeedsRecreation ) result += penalty;
            if ( t.Pool == TunnelConfig.TunnelPool.Exploratory ) result += penalty / 2.0;
            if ( !t.Active ) result += penalty;
            if ( t.Expired ) result += penalty;

            return result;
        }

        public InboundTunnel GetEstablishedInboundTunnel( bool allowexplo )
        {
            IEnumerable<Tunnel> tunnels;

            lock ( ClientsInbound )
            {
                tunnels = ClientsInbound.ToArray();
            }

            if ( allowexplo || !tunnels.Any() )
            {
                lock ( ExploratoryInbound )
                {
                    tunnels = tunnels.Concat( ExploratoryInbound ).ToArray();
                }
            }

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
            IEnumerable<Tunnel> tunnels;

            lock ( ClientsOutbound )
            {
                tunnels = ClientsOutbound.ToArray();
            }

            if ( allowexplo || !tunnels.Any() )
            {
                lock ( ClientsOutbound )
                {
                    tunnels = tunnels.Concat( ExploratoryOutbound ).ToArray();
                }
            }

            if ( tunnels.Any() )
            {
                return (OutboundTunnel)tunnels.RandomWeighted(
                    GenerateTunnelWeight, true, TunnelSelectionElitism );
            }

            return null;
        }

        public OutboundTunnel[] GetExploratoryOutboundTunnels()
        {
            lock ( ExploratoryOutbound )
            {
                return ExploratoryOutbound.Where( t => t.Active ).Select( t => (OutboundTunnel)t ).ToArray();
            }
        }

        public InboundTunnel[] GetExploratoryInboundTunnels()
        {
            lock ( ExploratoryInbound )
            {
                return ExploratoryInbound.Where( t => t.Active ).Select( t => (InboundTunnel)t ).ToArray();
            }
        }

        public IEnumerable<OutboundTunnel> GetOutboundTunnels()
        {
            IEnumerable<OutboundTunnel> result;
            lock ( ExploratoryOutbound )
            {
                result = ExploratoryOutbound.Where( t => t.Active ).Select( t => (OutboundTunnel)t );
            }

            lock ( ClientsOutbound )
            {
                result = result.Concat( ClientsOutbound.Where( t => t.Active ).Select( t => (OutboundTunnel)t ) );
            }
            return result;
        }

        public IEnumerable<InboundTunnel> GetInboundTunnels()
        {
            IEnumerable<InboundTunnel> result;

            lock ( ExploratoryInbound )
            {
                result = ExploratoryInbound.Where( t => t.Active ).Select( t => (InboundTunnel)t );
            }

            lock ( ClientsInbound )
            {
                result = result.Concat( ClientsInbound.Where( t => t.Active ).Select( t => (InboundTunnel)t ) );
            }

            return result;
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

                    while ( IncomingMessageQueue.Count > 0 )
                    {
                        II2NPHeader msg;
                        lock ( IncomingMessageQueue )
                        {
                            msg = IncomingMessageQueue.First.Value;
                            IncomingMessageQueue.RemoveFirst();
                        }

                        switch ( msg.MessageType )
                        {
                            case I2NPMessage.MessageTypes.DatabaseStore:
                                var ds = (DatabaseStoreMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.Log( "RunIncomingMessagePump: DatabaseStore : " + ds.Key.Id32Short );
                                //Logging.Log( "RunIncomingMessagePump: DatabaseStore : " + ds.ToString() );
#endif
                                HandleDatabaseStore( ds );
                                break;

                            case I2NPMessage.MessageTypes.DatabaseSearchReply:
                                var dsr = (DatabaseSearchReplyMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.Log( "TunnelProvider.RunIncomingMessagePump: DatabaseSearchReply: " + dsr.ToString() );
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
                                var tunnels = FindTunnelFromTunnelId( tg.TunnelId );
                                if ( tunnels != null && tunnels.Any() )
                                {
                                    foreach ( var tunnel in tunnels )
                                    {
#if LOG_ALL_TUNNEL_TRANSFER
                                        if ( FilterMessageTypes.Update( new HashedItemGroup( tg.TunnelId, 0xf4e8 ) ) )
                                        {
                                            Logging.Log( "RunIncomingMessagePump: " + tg.ToString() + "\r\n" + tunnel.ToString() );
                                        }
#endif
                                        tunnel.MessageReceived( I2NPMessage.ReadHeader16( (BufRefLen)tg.GatewayMessage ) );
                                    }
                                }
                                else
                                {
#if LOG_ALL_TUNNEL_TRANSFER
                                    if ( FilterMessageTypesShowMore.Update( new HashedItemGroup( tg.TunnelId, 0x1285 ) ) )
                                    {
                                        Logging.LogDebug( "RunIncomingMessagePump: Tunnel not found for TunnelGateway. Dropped. " + tg.ToString() );
                                    }
#endif
                                }
                                break;

                            case I2NPMessage.MessageTypes.TunnelData:
                                var td = (TunnelDataMessage)msg.Message;
                                tunnels = FindTunnelFromTunnelId( td.TunnelId );
                                if ( tunnels != null && tunnels.Any() )
                                {
                                    foreach ( var tunnel in tunnels )
                                    {
#if LOG_ALL_TUNNEL_TRANSFER
                                        if ( FilterMessageTypes.Update( new HashedItemGroup( tunnel, 0x6433 ) ) )
                                        {
                                            Logging.LogDebug( string.Format( "RunIncomingMessagePump: TunnelData ({0}): {1}.",
                                                td, tunnel ) );
                                        }
#endif
                                        tunnel.MessageReceived( msg );
                                    }
                                }
                                else
                                {
#if LOG_ALL_TUNNEL_TRANSFER
                                    if ( FilterMessageTypes.Update( new HashedItemGroup( td.TunnelId, 0x35c7 ) ) )
                                    {
                                        Logging.LogDebug( "RunIncomingMessagePump: Tunnel not found for TunnelData. Dropped. " + td.ToString() );
                                    }
#endif
                                }
                                break;

                            case I2NPMessage.MessageTypes.DeliveryStatus:
#if LOG_ALL_TUNNEL_TRANSFER
                                Logging.LogDebug( "TunnelProvider.RunIncomingMessagePump: DeliveryStatus: " + msg.Message.ToString() );
#endif

                                ThreadPool.QueueUserWorkItem( cb =>
                                {
                                    lock ( DeliveryStatusReceivedLock ) DeliveryStatusReceived?.Invoke( (DeliveryStatusMessage)msg.Message );
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

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 2 );
        ItemFilterWindow<HashedItemGroup> FilterMessageTypesShowMore = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 10 );
#endif

        AutoResetEvent IncommingMessageReceived = new AutoResetEvent( false );
        public void DistributeIncomingMessage( ITransport transp, II2NPHeader msg )
        {
#if LOG_ALL_TUNNEL_TRANSFER
            if ( FilterMessageTypes.Update( new HashedItemGroup( transp, (int)msg.MessageType ) ) )
            {
                Logging.LogDebug( string.Format( "TunnelProvider.DistributeIncommingMessage: ({0}) from {1}.",
                    msg.MessageType, 
                    transp == null ? "null" : 
                        ( transp.RemoteRouterIdentity == null ? "t-null": 
                            transp.RemoteRouterIdentity.IdentHash.Id32Short ) ) );
            }
#endif
            lock ( IncomingMessageQueue )
            {
                IncomingMessageQueue.AddLast( msg );
            }
            IncommingMessageReceived.Set();
        }


        #region TunnelIds

        void AddTunnelId( Tunnel tunnel, uint id )
        {
            lock ( TunnelIds )
            {
                if ( !TunnelIds.TryGetValue( id, out var tunnels ) )
                {
                    var newlist = new List<Tunnel>();
                    newlist.Add( tunnel );
                    TunnelIds[id] = newlist;
                }
                else
                {
                    tunnels.Add( tunnel );
                }
            }
        }

        void RemoveTunnelId( Tunnel tunnel, uint id )
        {
            lock ( TunnelIds )
            {
                if ( !TunnelIds.ContainsKey( id ) ) return;
                var idlist = TunnelIds[id];
                idlist.Remove( tunnel );
                if ( idlist.Count == 0 ) TunnelIds.Remove( id );
            }
        }

        private IEnumerable<Tunnel> FindTunnelFromTunnelId( uint tunnelid )
        {
            lock ( TunnelIds )
            {
                if ( !TunnelIds.ContainsKey( tunnelid ) ) return null;
                return TunnelIds[tunnelid].ToArray();
            }
        }
        #endregion

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
            var myident = RouterContext.Inst.MyRouterIdentity.IdentHash;
            var tome = records.Where( rec => myident.Hash16 == rec.ToPeer16 );

            if ( tome.Count() != 1 )
            {
                Logging.LogDebug( $"HandleTunnelBuildRecords: Failed to find a ToPeer16 record. ({tome.Count()})" );
                return;
            }

            var myrec = new EGBuildRequestRecord( tome.Single() );
            var drec = myrec.Decrypt( RouterContext.Inst.PrivateKey );

            if ( drec.OurIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                Logging.LogDebug( "HandleTunnelBuildRecords: Failed to full id hash match " + drec.ToString() );
                return;
            }

            if ( drec.ToAnyone || drec.FromAnyone || drec.NextIdent != RouterContext.Inst.MyRouterIdentity.IdentHash )
            {
                PassthroughMgr.HandleTunnelBuildRecords( msg, records, myrec, drec );
                return;
            }

            // Inbound tunnel build for me
            HandleIncomingTunnelBuildRecords( drec, records );
        }

#if DEBUG
        TimeWindowDictionary<uint, RefPair<TickCounter, int>> ReallyOldTunnelBuilds =
            new TimeWindowDictionary<uint, RefPair<TickCounter, int>>( TickSpan.Minutes( 10 ) );
#endif

        private void HandleIncomingTunnelBuildRecords( BuildRequestRecord drec, IList<AesEGBuildRequestRecord> records )
        {
            InboundTunnel[] tunnels;
            lock ( PendingInbound )
            {
                tunnels = PendingInbound
                    .Where( t => ( (InboundTunnel)t ).ReceiveTunnelId == drec.ReceiveTunnel )
                    .Select( t => (InboundTunnel)t )
                    .ToArray();
            }

            if ( tunnels.Length == 0 )
            {
                lock ( ExploratoryPendingInbound )
                {
                    tunnels = ExploratoryPendingInbound
                        .Where( t => ( (InboundTunnel)t ).ReceiveTunnelId == drec.ReceiveTunnel )
                        .Select( t => (InboundTunnel)t )
                        .ToArray();
                }
            }

            if ( tunnels.Length == 0 )
            {
#if DEBUG
                ReallyOldTunnelBuilds.ProcessItem( drec.NextTunnel, ( k, p ) =>
                {
                    Logging.LogDebug( $"Tunnel build req failed {drec.NextTunnel} age {p.Left.DeltaToNowMilliseconds / p.Right} msec / hop. Unknown tunnel id." );
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
                foreach ( var one in records ) recordcopies.Add( new AesEGBuildRequestRecord( new BufRef( one.Data.Clone() ) ) );

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

        internal static void UpdateTunnelBuildReply( IEnumerable<AesEGBuildRequestRecord> records, EGBuildRequestRecord myrec,
            BufLen replykey, BufLen replyiv, BuildResponseRecord.RequestResponse response )
        {
            myrec.Data.Randomize();
            var responserec = new BuildResponseRecord( myrec.Data )
            {
                Reply = response
            };
            responserec.UpdateHash();

            var cipher = new CbcBlockCipher( new AesEngine() );
            cipher.Init( true, replykey.ToParametersWithIV( replyiv ) );

            foreach ( var one in records )
            {
                cipher.Reset();
                one.Process( cipher );
            }
        }

        internal void HandleTunnelBuildReply( II2NPHeader16 header )
        {
            var any = FindPendingOutbound( header, PendingOutbound );
            any |= FindPendingOutbound( header, ExploratoryPendingOutbound );

#if DEBUG
            if ( !any )
            {
                ReallyOldTunnelBuilds.ProcessItem( header.MessageId, ( k, p ) =>
                    Logging.LogDebug( $"Tunnel build req failed {header.MessageId} age {p.Left.DeltaToNowMilliseconds / p.Right} msec / hop. MessageId unknown." )
                );
            }
#endif
        }

        private bool FindPendingOutbound( II2NPHeader16 header, List<Tunnel> queue )
        {
            bool matching = false;

            lock ( queue )
            {
                var matches = queue
                    .OfType<OutboundTunnel>()
                    .Where( ot => ot.TunnelBuildReplyMessageId == header.MessageId );
                foreach ( var match in matches )
                {
                    match.MessageReceived( header );
                    matching = true;
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.LogDebug( () => $"HandleTunnelBuildReply: MsgId match {header.MessageId:X8}." );
#endif
                }
            }

            return matching;
        }

        internal void OutboundTunnelEstablished( OutboundTunnel tunnel )
        {
            tunnel.EstablishedTime.SetNow();

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

            switch ( tunnel.Pool )
            {
                case TunnelConfig.TunnelPool.Exploratory:
                    RemoveTunnelFromPool( tunnel, ExploratoryPendingOutbound );
                    lock ( ExploratoryOutbound )
                    {
                        ExploratoryOutbound.Add( tunnel );
                    }
                    ExplorationMgr.TunnelEstablished( tunnel );
#if RUN_TUNNEL_TESTS
                    TunnelTester.Inst.Test( tunnel );
#endif
                    break;

                case TunnelConfig.TunnelPool.Client:
                    RemoveTunnelFromPool( tunnel, PendingOutbound );
                    lock ( ClientsOutbound )
                    {
                        ClientsOutbound.Add( tunnel );
                    }
                    ClientsMgr.TunnelEstablished( tunnel );
#if RUN_TUNNEL_TESTS
                    TunnelTester.Inst.Test( tunnel );
#endif
                    break;

                case TunnelConfig.TunnelPool.External:
                    RemoveTunnelFromPool( tunnel, PendingOutbound );
                    PassthroughMgr.TunnelEstablished( tunnel );
                    break;

                default: throw new NotImplementedException();
            }
        }

        internal void InboundTunnelEstablished( InboundTunnel tunnel )
        {
            tunnel.EstablishedTime.SetNow();

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

            RemoveTunnelFromPool( tunnel, PendingInbound );
            RemoveTunnelFromPool( tunnel, ExploratoryPendingInbound );

            switch ( tunnel.Pool )
            {
                case TunnelConfig.TunnelPool.Exploratory:
                    lock ( ExploratoryInbound )
                    {
                        ExploratoryInbound.Add( tunnel );
                    }
                    ExplorationMgr.TunnelEstablished( tunnel );
#if RUN_TUNNEL_TESTS
                    TunnelTester.Inst.Test( tunnel );
#endif
                    break;

                case TunnelConfig.TunnelPool.Client:
                    lock ( ClientsInbound )
                    {
                        ClientsInbound.Add( tunnel );
                    }
                    ClientsMgr.TunnelEstablished( tunnel );
#if RUN_TUNNEL_TESTS
                    TunnelTester.Inst.Test( tunnel );
#endif
                    break;

                case TunnelConfig.TunnelPool.External:
                    PassthroughMgr.TunnelEstablished( tunnel );
                    break;

                default: throw new NotImplementedException();
            }
        }

        internal void TunnelTestFailed( Tunnel tunnel )
        {
            switch ( tunnel.Pool )
            {
                case TunnelConfig.TunnelPool.Exploratory:
                    ExplorationMgr.TunnelTimeout( tunnel );
                    break;

                case TunnelConfig.TunnelPool.Client:
                    ClientsMgr.TunnelTimeout( tunnel );
                    break;
            }

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
