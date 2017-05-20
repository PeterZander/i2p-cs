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

            Worker = new Thread( () => Run() );
            Worker.Name = "TransportProvider";
            Worker.IsBackground = true;
            Worker.Start();
        }

        public static void Start()
        {
            if ( Inst != null ) return;
            Inst = new TransportProvider();
        }

        PeriodicLogger ActiveConnectionLog = new PeriodicLogger( DebugUtils.LogLevels.Information, 15 );
        PeriodicAction DropOldExceptions = new PeriodicAction( TickSpan.Minutes( ExeptionHistoryLifetimeMinutes ) );

        bool Terminated = false;

        SSUHost SsuHost = null;
        public int SsuHostBlockedIPCount { get { return SsuHost.BlockedIPCount; } }

        private void Run()
        {
            try
            {
                var ntcphost = new NTCPHost();

                ntcphost.ConnectionCreated += new Action<ITransport>( ntcphost_ConnectionCreated );
                SsuHost.ConnectionCreated += new Action<ITransport>( SsuHost_ConnectionCreated );

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
                                DebugUtils.LogDebug( () => "TransportProvider: Destination " + found.Destination.Id32Short + " found. Sending data." );
                                TransportProvider.Send( found.Destination, msg );
                            }
                        }

                        ActiveConnectionLog.Log( () => string.Format( "TransportProvider: Running: {0}. Established: {1}.",
                            RunningTransports.Count,
                            EstablishedTransports.Count ) );

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
                        DebugUtils.Log( ex );
                    }
                    catch ( Exception ex )
                    {
                        DebugUtils.Log( ex );
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
                var matches = EstablishedTransports.Where( t => object.ReferenceEquals( t.Value, instance ) ).Select( t => t.Key ).ToArray();
                foreach ( var key in matches ) EstablishedTransports.Remove( key );
            }
            lock ( RunningTransports )
            {
                var matches = RunningTransports.Where( t => object.ReferenceEquals( t.Value, instance ) ).Select( t => t.Key ).ToArray();
                foreach ( var key in matches ) RunningTransports.Remove( key );
            }

            instance.Terminate();
        }

        Dictionary<I2PIdentHash, ITransport> RunningTransports = new Dictionary<I2PIdentHash, ITransport>();
        Dictionary<I2PIdentHash, ITransport> EstablishedTransports = new Dictionary<I2PIdentHash, ITransport>();

        public void Disconnect( I2PIdentHash dest )
        {
            var t = GetEstablishedTransport( dest, false );
            if ( t == null ) return;

            t.Terminate();
        }

        object TsSearchLock = new object();

        protected ITransport GetEstablishedTransport( I2PIdentHash dest, bool create )
        {
            lock ( TsSearchLock )
            {
                lock ( EstablishedTransports )
                {
                    if ( EstablishedTransports.ContainsKey( dest ) )
                    {
                        var result = EstablishedTransports[dest];
                        if ( result == null ) DebugUtils.LogDebug( () =>
                            string.Format( "TransportProvider: GetEstablishedTransport: WARNING! EstablishedTransports contains null ref for {0}!",
                            dest.Id32Short ) );
                        return result;
                    }
                }

                lock ( RunningTransports )
                {
                    if ( RunningTransports.ContainsKey( dest ) )
                    {
                        var result = RunningTransports[dest];
                        if ( result == null ) DebugUtils.LogDebug( () =>
                            string.Format( "TransportProvider: GetEstablishedTransport: WARNING! RunningTransports contains null ref for {0}!",
                            dest.Id32Short ) );
                        return result;
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
                I2PRouterAddress ra_ntcp = null;
                var ntcpaddr = ri.Adresses.Where( a => ( a.TransportStyle == "NTCP" ) &&
                            a.Options.Contains( "host" ) &&
                            a.Options.Contains( "port" ) &&
                            ( RouterContext.Inst.UseIpV6 || a.Options["host"].Contains( '.' ) ) );
                var a1 = ntcpaddr.Where( a => GetAddressFamiliy( a, "host" ) == System.Net.Sockets.AddressFamily.InterNetwork );
                if ( a1.Any() )
                {
                    ra_ntcp = a1.Random();
                }
                else
                {
                    a1 = ntcpaddr.Where( a => Dns.GetHostEntry( a.Options["host"] ).AddressList.
                        Any( aa => aa.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ) );
                    if ( a1.Any() )
                    {
                        ra_ntcp = a1.Random();
                    }
                }

                I2PRouterAddress ra_ssu = null;
                var ssuaddr = ri.Adresses.Where( a => ( a.TransportStyle == "SSU" ) && a.Options.Contains( "key" ) );
                a1 = ssuaddr.Where( a => GetAddressFamiliy( a, "host" ) == System.Net.Sockets.AddressFamily.InterNetwork ||
                    GetAddressFamiliy( a, "ihost0" ) == System.Net.Sockets.AddressFamily.InterNetwork );
                if ( a1.Any() )
                {
                    ra_ssu = a1.Random();
                }
                else
                {
                    a1 = ntcpaddr.Where( a => Dns.GetHostEntry( a.Options["host"] ).AddressList.
                        Any( aa => aa.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ) );
                    if ( a1.Any() )
                    {
                        ra_ssu = a1.Random();
                    }
                    else
                    {
                        a1 = ntcpaddr.Where( a => Dns.GetHostEntry( a.Options["ihost0"] ).AddressList.
                            Any( aa => aa.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ) );
                        if ( a1.Any() )
                        {
                            ra_ssu = a1.Random();
                        }
                    }
                }

                I2PRouterAddress ra;

                //if ( ra_ntcp != null ) ra = ra_ntcp; else ra = ra_ssu;
                if ( ra_ssu != null ) ra = ra_ssu; else ra = ra_ntcp;

                if ( ra == null )
                {
                    DebugUtils.LogDebug( () =>
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

                DebugUtils.LogDebug( () =>
                    string.Format( "TransportProvider: Creating new {0} transport {2} to {1}",
                    ra.TransportStyle, ri.Identity.IdentHash.Id32Short, transport.DebugId ) );

                AddTransport( transport, ri.Identity.IdentHash );
                transport.Connect();

                var dstore = new DatabaseStoreMessage( RouterContext.Inst.MyRouterInfo );
                transport.Send( dstore );
            }
            catch ( Exception ex )
            {
#if LOG_ALL_TRANSPORT
                DebugUtils.Log( ex );
                DebugUtils.Log( "TransportProvider: CreateTransport stack trace: " + System.Environment.StackTrace );
#else
                DebugUtils.LogDebug( () => "TransportProvider: Exception [" + ex.GetType().ToString() + "] '" + ex.Message + "' to " +
                    ri.Identity.IdentHash.Id32Short + "." );
#endif
                if ( transport != null ) Remove( transport );
                throw;
            }

            return transport;
        }

        private void AddTransport( ITransport transport, I2PIdentHash ih )
        {
            transport.ConnectionShutDown += new Action<ITransport>( transport_ConnectionShutDown );
            transport.ConnectionEstablished += new Action<ITransport>( transport_ConnectionEstablished );

            transport.DataBlockReceived += new Action<ITransport, II2NPHeader>( transport_DataBlockReceived );
            transport.ConnectionException += new Action<ITransport, Exception>( transport_ConnectionException );

            if ( ih != null ) lock ( RunningTransports )
            {
                RunningTransports[ih] = transport;
            }
        }

        #region Provider events

        void SsuHost_ConnectionCreated( ITransport transport )
        {
            DebugUtils.LogDebug( () =>
                string.Format( "TransportProvider: SSU incoming transport {0} added.",
                transport.DebugId ) );

            if ( transport.RemoteRouterIdentity != null )
            {
                AddTransport( transport, transport.RemoteRouterIdentity.IdentHash );
            }
            else
            {
                AddTransport( transport, null );
            }
        }

        void ntcphost_ConnectionCreated( ITransport transport )
        {
            DebugUtils.LogDebug( () =>
                string.Format( "TransportProvider: NTCP incoming transport {0} added.",
                transport.DebugId ) );

            if ( transport.RemoteRouterIdentity != null )
            {
                AddTransport( transport, transport.RemoteRouterIdentity.IdentHash );
            }
            else
            {
                AddTransport( transport, null );
            }
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
                DebugUtils.Log( ex );
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
                DebugUtils.Log( ex );
            }
        }

        void transport_ConnectionEstablished( ITransport instance )
        {
            var hash = instance.RemoteRouterIdentity.IdentHash;

            DebugUtils.LogDebug( () =>
                string.Format( "TransportProvider: transport_ConnectionEstablished: {0} to {1}.", instance.DebugId, hash.Id32Short ) );

            lock ( EstablishedTransports )
            {
                EstablishedTransports[hash] = instance;
            }
        }

        void transport_ConnectionShutDown( ITransport instance )
        {
            DebugUtils.LogDebug( () =>
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

        private static bool Send( I2PIdentHash dest, I2NPMessage data, int reclvl )
        {
            ITransport transp = null;
            try
            {
                if ( dest == RouterContext.Inst.MyRouterIdentity.IdentHash )
                {
                    DebugUtils.LogDebug( () => "TransportProvider: Loopback " + data.ToString() );
                    TransportProvider.Inst.DistributeIncomingMessage( null, data.Header16 );
                    return true;
                }

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
                    if ( transp == null ) throw new ArgumentException( "Unable to contact " + dest.ToString() );
                    transp.Send( data );
                }
                else
                {
                    if ( TransportProvider.Inst.CurrentlyUnresolvableRouters.Contains( dest ) ) throw new ArgumentException( "Unable to resolve " + dest.ToString() );

                    TransportProvider.Inst.CurrentlyUnknownRouters.Add( dest, data );
                }
            }
            catch ( FailedToConnectException ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null && NetDb.Inst != null ) NetDb.Inst.Statistics.FailedToConnect( dest );
                DebugUtils.LogDebug( () => "TransportProvider.Send: " + ( transp == null ? "<>" : transp.DebugId ) +
                    " Exception " + ex.GetType().ToString() + ", " + ex.Message );

                throw;
            }
            catch ( EndOfStreamEncounteredException ex )
            {
                TransportProvider.Inst.Remove( transp );

                DebugUtils.LogDebug( () => "TransportProvider.Send: Connection " + ( transp == null ? "<>" : transp.DebugId ) +
                    " closed exception: " + ex.GetType().ToString() + ", " + ex.Message );

                if ( reclvl > 1 || !Send( dest, data, reclvl + 1 ) )
                {
                    DebugUtils.LogDebug( () => "TransportProvider.Send: Recconnection failed to " + dest.Id32Short + ", reclvl: " + reclvl.ToString() + "." );
                    throw;
                }
            }
            catch ( RouterUnresolvableException ex )
            {
                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                DebugUtils.LogDebug( () => "TransportProvider.Send: Unresolvable router: " + ex.Message );

                throw;
            }
            catch ( Exception ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                DebugUtils.LogDebug( () => "TransportProvider.Send: Exception " + ex.GetType().ToString() + ", " + ex.Message );

                throw;
            }
            return true;
        }
    }
}
