using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Transport.NTCP;
using System.Threading;
using I2PCore.Utils;
using I2PCore.Tunnel;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Tunnel.I2NP.Messages;
using System.Net;
using I2PCore.Router;
using I2PCore.Transport.SSU;
using System.Net.Sockets;

namespace I2PCore.Transport
{
    public class TransportProvider
    {
        protected static Thread Worker;
        public static TransportProvider Inst { get; protected set; }

        public const int ExeptionHistoryLifetimeMinutes = 20;

        UnresolvableRouters CurrentlyUnresolvableRouters = new UnresolvableRouters();
        public int CurrentlyUnresolvableRoutersCount { get { return CurrentlyUnresolvableRouters.Count; } }

        UnknownRouterQueue CurrentlyUnknownRouters;
        public int CurrentlyUnknownRoutersCount { get { return CurrentlyUnknownRouters.Count; } }

        internal class ExceptionHistoryInstance
        {
            internal TickCounter Generated = new TickCounter();
            internal Exception Error;
            internal IPAddress Address;
        }
        Dictionary<IPAddress, ExceptionHistoryInstance> AddressesWithExceptions = new Dictionary<IPAddress, ExceptionHistoryInstance>();
        public int AddressesWithExceptionsCount { get { return AddressesWithExceptions.Count; } }

        public event Action<ITransport, II2NPHeader> IncomingMessage;

        TransportProvider()
        {
            CurrentlyUnknownRouters = new UnknownRouterQueue( CurrentlyUnresolvableRouters );

            SsuHost = new SSUHost( RouterContext.Inst, NetDb.Inst.Statistics );

            Worker = new Thread( Run )
            {
                Name = "TransportProvider",
                IsBackground = true
            };
            Worker.Start();
        }

        public static void Start()
        {
            if ( Inst != null ) return;
            Inst = new TransportProvider();
        }

        PeriodicAction ActiveConnectionLog = new PeriodicAction( TickSpan.Seconds( 15 ) );
        PeriodicAction DropOldExceptions = new PeriodicAction( TickSpan.Minutes( ExeptionHistoryLifetimeMinutes ) );

        bool Terminated = false;

        SSUHost SsuHost = null;
        public int SsuHostBlockedIPCount { get { return SsuHost.BlockedIPCount; } }

        private void Run()
        {
            try
            {
                var ntcphost = new NTCPHost();

                ntcphost.ConnectionCreated += NTCPhost_ConnectionCreated;
                SsuHost.ConnectionCreated += SSUHost_ConnectionCreated;

                while ( !Terminated )
                {
                    try
                    {
                        Thread.Sleep( 1000 );

                        var known = CurrentlyUnknownRouters.FindKnown();
                        foreach ( var found in known )
                        {
                            foreach ( var msg in found.Messages )
                            {
                                Logging.LogTransport( "TransportProvider: Destination " + found.Destination.Id32Short + " found. Sending data." );
                                TransportProvider.Send( found.Destination, msg );
                            }
                        }

#if DEBUG
                        ActiveConnectionLog.Do( () =>
                        {
                            var ntcprunning = RunningTransports.Where( t => t.Protocol == "NTCP" ).ToArray();
                            var ssurunning = RunningTransports.Where( t => t.Protocol == "SSU" ).ToArray();
                            var ntcpestablished = EstablishedTransports.SelectMany( t => t.Value.Where( t2 => t2.Protocol == "NTCP" ) ).ToArray();
                            var ssuestablished = EstablishedTransports.SelectMany( t => t.Value.Where( t2 => t2.Protocol == "SSU" ) ).ToArray();

                            Logging.LogDebug(
                                $"TransportProvider: Established out/in " +
                                $"({ssuestablished.Count( t => t.Outgoing )}/{ssurunning.Count( t => t.Outgoing )})/" +
                                $"({ssuestablished.Count( t => !t.Outgoing )}/{ssurunning.Count( t => !t.Outgoing )}) SSU, " +
                                $"({ntcpestablished.Count( t => t.Outgoing )}/{ntcprunning.Count( t => t.Outgoing )})/" +
                                $"({ntcpestablished.Count( t => !t.Outgoing )}/{ntcprunning.Count( t => !t.Outgoing )}) NTCP." );
                        } );
#endif

                        DropOldExceptions.Do( delegate
                        {
                            lock ( AddressesWithExceptions )
                            {
                                var remove = AddressesWithExceptions.Where( eh =>
                                    eh.Value.Generated.DeltaToNow.ToMinutes >= ExeptionHistoryLifetimeMinutes ).Select( eh => eh.Key ).ToArray();
                                foreach ( var one in remove ) AddressesWithExceptions.Remove( one );
                            }
                        } );
                    }
                    catch ( ThreadAbortException ex )
                    {
                        Logging.Log( ex );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
            }
            finally
            {
                Terminated = true;
            }
        }

        private void Remove( ITransport instance )
        {
            if ( instance == null ) return;

            lock ( EstablishedTransports )
            {
                foreach( var one in EstablishedTransports.ToArray() )
                {
                    one.Value.Remove( instance );
                    if ( !one.Value.Any() )
                    {
                        EstablishedTransports.Remove( one.Key );
                    }
                }
            }
            lock ( RunningTransports )
            {
                RunningTransports.Remove( instance );
            }

            instance.Terminate();
        }

        List<ITransport> RunningTransports = new List<ITransport>();

        Dictionary<I2PIdentHash, HashSet<ITransport>> EstablishedTransports = 
            new Dictionary<I2PIdentHash, HashSet<ITransport>>();

        public void Disconnect( I2PIdentHash dest )
        {
            var t = GetEstablishedTransport( dest, false );
            if ( t == null ) return;

            t.Terminate();
        }

        readonly object TsSearchLock = new object();

        protected ITransport GetEstablishedTransport( I2PIdentHash dest, bool create )
        {
            lock ( TsSearchLock )
            {
                lock ( EstablishedTransports )
                {
                    if ( EstablishedTransports.ContainsKey( dest ) )
                    {
                        var result = EstablishedTransports[dest];
                        if ( result == null ) Logging.LogTransport(
                            string.Format( "TransportProvider: GetEstablishedTransport: WARNING! EstablishedTransports contains null ref for {0}!",
                            dest.Id32Short ) );
                        return result.FirstOrDefault();
                    }
                }

                if ( create )
                {
                    var ri = NetDb.Inst[dest];
                    if ( ri.Identity.IdentHash != dest ) throw new ArgumentException( "NetDb mismatch. Search for " +
                        dest.Id32 + " returns " + ri.Identity.IdentHash.Id32 );
                    return CreateTransport( ri );
                }
            }

            return null;
        }

        public ITransport GetTransport( I2PIdentHash dest )
        {
            return GetEstablishedTransport( dest, true );
        }

        AddressFamily GetAddressFamiliy( I2PRouterAddress addr, string option )
        {
            if ( !addr.Options.Contains( option ) ) return AddressFamily.Unknown;
            return I2PRouterAddress.IPTestHostName( addr.Options[option] );
        }

        private ITransport CreateTransport( I2PRouterInfo ri )
        {
            ITransport transport = null;

            try
            {
                var ntcpaddr = ri.Adresses.Where( a => ( a.TransportStyle == "NTCP" ) 
                            && a.HaveHostAndPort
                            && ( RouterContext.Inst.UseIpV6 
                                || a.Options.ValueContains( "host", "." ) ) );

                var ssuaddr = ri.Adresses.Where( a => ( a.TransportStyle == "SSU" ) 
                            && a.Options.Contains( "key" ) 
                            && ( RouterContext.Inst.UseIpV6 
                                || a.Options.ValueContains( "host", "." )
                                || a.Options.ValueContains( "ihost0", "." ) ) );

                I2PRouterAddress ra = ssuaddr
                    .Where( a => a.Options.Contains( "host" ) )
                    .Random() 
                        ?? ntcpaddr.Random() 
                        ?? ssuaddr.Random();

                if ( ra == null )
                {
                    Logging.LogTransport(
                        string.Format( "TransportProvider: CreateTransport: No usable address found for {0}!", ri.Identity.IdentHash.Id32Short ) );
                    return null;
                }

                switch ( ra.TransportStyle.ToString() )
                {
                    case "SSU":
                        transport = SsuHost.AddSession( ra, ri.Identity );
                        break;

                    case "NTCP":
                        transport = new NTCPClientOutgoing( ra, ri.Identity );
                        break;

                    default:
                        throw new NotImplementedException();
                }

                Logging.LogTransport(
                    string.Format( "TransportProvider: Creating new {0} transport {2} to {1}",
                    ra.TransportStyle, ri.Identity.IdentHash.Id32Short, transport.DebugId ) );

                AddTransport( transport );
                transport.Connect();

                var dstore = new DatabaseStoreMessage( RouterContext.Inst.MyRouterInfo );
                transport.Send( dstore );
            }
            catch ( Exception ex )
            {
#if LOG_MUCH_TRANSPORT
                Logging.LogTransport( ex );
                Logging.LogTransport( "TransportProvider: CreateTransport stack trace: " + System.Environment.StackTrace );
#else
                Logging.LogTransport( "TransportProvider: Exception [" + ex.GetType().ToString() + "] '" + ex.Message + "' to " +
                    ri.Identity.IdentHash.Id32Short + "." );
#endif
                if ( transport != null ) Remove( transport );
                throw;
            }

            return transport;
        }

        private void AddTransport( ITransport transport )
        {
            transport.ConnectionShutDown += transport_ConnectionShutDown;
            transport.ConnectionEstablished += transport_ConnectionEstablished;

            transport.DataBlockReceived += transport_DataBlockReceived;
            transport.ConnectionException += transport_ConnectionException;

            AddToRunningTransports( transport );
        }

        private void AddToRunningTransports( ITransport transport )
        {
            lock ( RunningTransports )
            {
                RunningTransports.Add( transport );
            }
        }

        private void AddToEstablishedTransports( I2PIdentHash ih, ITransport transport )
        {
            if ( ih != null )
            {
                lock ( EstablishedTransports )
                {
                    if ( EstablishedTransports.TryGetValue( ih, out var tr ) )
                    {
                        tr.Add( transport );
                    }
                    else
                    {
                        EstablishedTransports[ih] = new HashSet<ITransport>(
                            new ITransport[] { transport } );
                    }
                }
            }
        }

        #region Provider events

        void SSUHost_ConnectionCreated( ITransport transport )
        {
            Logging.LogTransport(
                string.Format( "TransportProvider: SSU incoming transport {0} added.",
                transport.DebugId ) );

            AddTransport( transport );
        }

        void NTCPhost_ConnectionCreated( ITransport transport )
        {
            Logging.LogTransport(
                string.Format( "TransportProvider: NTCP incoming transport {0} added.",
                transport.DebugId ) );

            AddTransport( transport );
        }

        void transport_ConnectionException( ITransport instance, Exception exinfo )
        {
            if ( instance.RemoteAddress == null ) return;

            try
            {
                lock ( AddressesWithExceptions )
                {
                    AddressesWithExceptions[instance.RemoteAddress] = new ExceptionHistoryInstance() 
                    { 
                        Error = exinfo, 
                        Address = instance.RemoteAddress 
                    };
                }
                if ( instance.RemoteRouterIdentity != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( instance.RemoteRouterIdentity.IdentHash );
                instance.Terminate();
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
        }

        void transport_DataBlockReceived( ITransport instance, II2NPHeader msg )
        {
            try
            {
                DistributeIncomingMessage( instance, msg );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
        }

        void transport_ConnectionEstablished( ITransport instance, I2PIdentHash hash )
        {
            if ( hash is null )
            {
                throw new ArgumentException( "TransportProvider: ConnectionEstablished ID hash required!" );
            }

            Logging.LogTransport(
                string.Format( "TransportProvider: transport_ConnectionEstablished: {0} to {1}.", instance.DebugId, hash.Id32Short ) );

            AddToEstablishedTransports( hash, instance );
        }

        void transport_ConnectionShutDown( ITransport instance )
        {
            Logging.LogTransport(
                string.Format( "TransportProvider: transport_ConnectionShutDown: {0}", instance.DebugId ) );

            Remove( instance );
        }

        internal void DistributeIncomingMessage( ITransport instance, II2NPHeader msg ) 
        {
            if ( IncomingMessage != null ) lock ( IncomingMessage ) IncomingMessage( instance, msg );
        }

        #endregion

        public static bool Send( I2PIdentHash dest, I2NPMessage data )
        {
            return Send( dest, data, 0 );
        }

        readonly static object TransportSelectionLock = new object();

        private static bool Send( I2PIdentHash dest, I2NPMessage data, int reclvl )
        {
            ITransport transp = null;
            try
            {
                if ( dest == RouterContext.Inst.MyRouterIdentity.IdentHash )
                {
                    Logging.LogTransport( "TransportProvider: Loopback " + data.ToString() );
                    TransportProvider.Inst.DistributeIncomingMessage( null, data.Header16 );
                    return true;
                }

                lock ( TransportSelectionLock )
                {
                    if ( TransportProvider.Inst.CurrentlyUnknownRouters.Contains( dest ) )
                    {
                        TransportProvider.Inst.CurrentlyUnknownRouters.Add( dest, data );
                        return true;
                    }

                    transp = TransportProvider.Inst.GetEstablishedTransport( dest, false );
                    if ( transp != null )
                    {
                        transp.Send( data );
                        return true;
                    }

                    if ( NetDb.Inst.Contains( dest ) )
                    {
                        transp = TransportProvider.Inst.GetTransport( dest );
                        if ( transp == null ) throw new FailedToConnectException( $"Unable to contact {dest}" );
                        transp.Send( data );
                    }
                    else
                    {
                        if ( TransportProvider.Inst.CurrentlyUnresolvableRouters.Contains( dest ) ) throw new ArgumentException( "Unable to resolve " + dest.ToString() );

                        TransportProvider.Inst.CurrentlyUnknownRouters.Add( dest, data );
                    }
                }
            }
            catch ( FailedToConnectException ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null && NetDb.Inst != null ) NetDb.Inst.Statistics.FailedToConnect( dest );
                Logging.LogTransport( "TransportProvider.Send: " + ( transp == null ? "<>" : transp.DebugId ) +
                    " Exception " + ex.GetType().ToString() + ", " + ex.Message );

                throw;
            }
            catch ( EndOfStreamEncounteredException ex )
            {
                TransportProvider.Inst.Remove( transp );

                Logging.LogTransport( "TransportProvider.Send: Connection " + ( transp == null ? "<>" : transp.DebugId ) +
                    " closed exception: " + ex.GetType().ToString() + ", " + ex.Message );

                if ( reclvl > 1 || !Send( dest, data, reclvl + 1 ) )
                {
                    Logging.LogTransport( "TransportProvider.Send: Recconnection failed to " + dest.Id32Short + ", reclvl: " + reclvl.ToString() + "." );
                    throw;
                }
            }
            catch ( RouterUnresolvableException ex )
            {
                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                Logging.LogTransport( "TransportProvider.Send: Unresolvable router: " + ex.Message );

                throw;
            }
            catch ( Exception ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                Logging.LogTransport( "TransportProvider.Send: Exception " + ex.GetType().ToString() + ", " + ex.Message );

                throw;
            }
            return true;
        }
    }
}
