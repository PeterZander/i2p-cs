using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using I2PCore.TransportLayer.NTCP;
using System.Threading;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using System.Net;
using I2PCore.SessionLayer;
using System.Net.Sockets;
using static I2PCore.Utils.BufUtils;
using System.Collections.Concurrent;

namespace I2PCore.TransportLayer
{
    public class TransportProvider
    {
        protected static Thread Worker;
        public static TransportProvider Inst { get; protected set; }

        public static readonly TickSpan ExeptionHistoryLifetime = TickSpan.Minutes( 20 );

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

        readonly ITransportProtocol[] TransportProtocols;

        TransportProvider()
        {
            CurrentlyUnknownRouters = new UnknownRouterQueue( CurrentlyUnresolvableRouters );

            TransportProtocols = GetTransportProtocols();

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
        PeriodicAction DropOldExceptions = new PeriodicAction( ExeptionHistoryLifetime );

        bool Terminated = false;

        public int SsuHostBlockedIPCount { get { return TransportProtocols.Sum( tp => tp.BlockedRemoteAddressesCount ); } }

        private void Run()
        {
            try
            {
                foreach ( var tp in TransportProtocols )
                {
                    tp.ConnectionCreated += TransportProtocol_ConnectionCreated;
                }

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
                                Logging.LogTransport( $"TransportProvider: Destination {found.Destination.Id32Short} found. Sending data." );
                                TransportProvider.Send( found.Destination, msg );
                            }
                        }

                        ActiveConnectionLog.Do( () =>
                        {
                            if ( Logging.LogLevel > Logging.LogLevels.Information ) return;

                            ITransport[] rt;
                            lock ( RunningTransports )
                            {
                                rt = RunningTransports.ToArray();
                            }

                            var et = EstablishedTransports.ToArray();

                            var ntcprunning = rt.Where( t => t.Protocol == "NTCP" ).ToArray();
                            var ssurunning = rt.Where( t => t.Protocol == "SSU" ).ToArray();
                            var ntcpestablished = et.SelectMany( t => t.Value.Where( t2 => t2.Key.Protocol == "NTCP" ) ).ToArray();
                            var ssuestablished = et.SelectMany( t => t.Value.Where( t2 => t2.Key.Protocol == "SSU" ) ).ToArray();

                            Logging.LogInformation(
                                $"TransportProvider: (Established out/Running out)/(Established in/Running in) " +
                                $"({ssuestablished.Count( t => t.Key.Outgoing )}/{ssurunning.Count( t => t.Outgoing )})/" +
                                $"({ssuestablished.Count( t => !t.Key.Outgoing )}/{ssurunning.Count( t => !t.Outgoing )}) SSU, " +
                                $"({ntcpestablished.Count( t => t.Key.Outgoing )}/{ntcprunning.Count( t => t.Outgoing )})/" +
                                $"({ntcpestablished.Count( t => !t.Key.Outgoing )}/{ntcprunning.Count( t => !t.Outgoing )}) NTCP." );

#if DEBUG
                            var ntcpsent = ntcprunning.Sum( t => t.BytesSent );
                            var ntcprecv = ntcprunning.Sum( t => t.BytesReceived );
                            var ssusent = ssurunning.Sum( t => t.BytesSent );
                            var ssurecv = ssurunning.Sum( t => t.BytesReceived );

                            Logging.LogDebug(
                                $"TransportProvider: send / recv  " +
                                $"SSU: {BytesToReadable( ssusent ),12} / {BytesToReadable( ssurecv ),12}    " +
                                $"NTCP: {BytesToReadable( ntcpsent ),12} / {BytesToReadable( ntcprecv ),12}" );
#endif
                        } );

                        DropOldExceptions.Do( delegate
                        {
                            lock ( AddressesWithExceptions )
                            {
                                var remove = AddressesWithExceptions.Where( eh =>
                                                eh.Value.Generated.DeltaToNow > ExeptionHistoryLifetime )
                                    .Select( eh => eh.Key )
                                    .ToArray();

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

        ITransportProtocol[] GetTransportProtocols()
        {
            var protocols = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany( a => a.GetTypes()
                                .Where( t => t.IsDefined( 
                                                typeof( TransportProtocolAttribute ),
                                                false )
                                             && typeof( ITransportProtocol ).IsAssignableFrom( t ) ) );

            return protocols
                    .Select( Activator.CreateInstance )
                    .Cast<ITransportProtocol>()
                    .ToArray();
        }

        private void Remove( ITransport instance )
        {
            if ( instance == null ) return;

            foreach( var one in EstablishedTransports.ToArray() )
            {
                one.Value.TryRemove( instance, out _ );
                if ( one.Value.IsEmpty )
                {
                    EstablishedTransports.TryRemove( one.Key, out _ );
                }
            }

            lock ( RunningTransports )
            {
                RunningTransports.Remove( instance );
            }

            instance.Terminate();
        }

        List<ITransport> RunningTransports = new List<ITransport>();

        ConcurrentDictionary<I2PIdentHash, ConcurrentDictionary<ITransport,byte>> EstablishedTransports = 
            new ConcurrentDictionary<I2PIdentHash, ConcurrentDictionary<ITransport,byte>>();

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
                if ( EstablishedTransports.TryGetValue( dest, out var result ) )
                {
                    if ( result == null )
                    {
                        Logging.LogTransport(
                            $"TransportProvider: GetEstablishedTransport: WARNING! " +
                            $"EstablishedTransports contains null ref for {dest.Id32Short}!" );
                    }
                    return result.FirstOrDefault().Key;
                }

                if ( create )
                {
                    var ri = NetDb.Inst[dest];
                    if ( ri.Identity.IdentHash != dest ) throw new ArgumentException( $"NetDb mismatch. Search for " +
                        $"{dest.Id32} returns {ri?.Identity?.IdentHash.Id32}" );
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
                var pproviders = TransportProtocols
                                    .Select( tp => new
                                    {
                                        Provider = tp,
                                        Capability = tp.ContactCapability( ri )
                                    } )
                                    .Where( tp => tp.Capability != ProtocolCapabilities.None )
                                    .GroupBy( tp => tp.Capability )
                                    .OrderByDescending( cc => (int)cc.Key );

                var pprovider = pproviders.FirstOrDefault()?.Random();

                if ( pprovider == null )
                {
                    Logging.LogTransport(
                        $"TransportProvider: CreateTransport: No usable address found for {ri.Identity.IdentHash.Id32Short}!" );
                    return null;
                }

                transport = pprovider.Provider.AddSession( ri );

                Logging.LogTransport( $"TransportProvider: Creating new {transport} to {ri.Identity.IdentHash.Id32Short}" );

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
                Logging.LogTransport( $"TransportProvider: Exception [{ex.GetType()}] " +
                    $"'{ex.Message}' to {ri.Identity.IdentHash.Id32Short}." );
#endif
                if ( transport != null ) Remove( transport );
                throw;
            }

            return transport;
        }

        private void AddTransport( ITransport transport )
        {
            transport.ConnectionShutDown += Transport_ConnectionShutDown;
            transport.ConnectionEstablished += Transport_ConnectionEstablished;

            transport.DataBlockReceived += Transport_DataBlockReceived;
            transport.ConnectionException += Transport_ConnectionException;

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
                if ( EstablishedTransports.TryGetValue( ih, out var tr ) )
                {
                    tr[transport] = 1;
                }
                else
                {
                    EstablishedTransports[ih] = new ConcurrentDictionary<ITransport, byte>(
                        new KeyValuePair<ITransport,byte>[] { new KeyValuePair<ITransport, byte>( transport, 1)  } );
                }
            }
        }

        #region Provider events

        void TransportProtocol_ConnectionCreated( ITransport transport )
        {
            Logging.LogTransport(
                $"TransportProvider: incoming transport {transport.DebugId} added." );

            AddTransport( transport );
        }

        void Transport_ConnectionException( ITransport instance, Exception exinfo )
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
                Logging.LogTransport(
                    $"TransportProvider: exception in {instance.DebugId} {ex.GetType().Name}" );
            }
        }

        void Transport_DataBlockReceived( ITransport instance, II2NPHeader msg )
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

        void Transport_ConnectionEstablished( ITransport instance, I2PIdentHash hash )
        {
            if ( hash is null )
            {
                throw new ArgumentException( "TransportProvider: ConnectionEstablished ID hash required!" );
            }

            Logging.LogTransport(
                $"TransportProvider: transport_ConnectionEstablished: {instance.DebugId} to {hash.Id32Short}." );

            AddToEstablishedTransports( hash, instance );
        }

        void Transport_ConnectionShutDown( ITransport instance )
        {
            Logging.LogTransport(
                $"TransportProvider: transport_ConnectionShutDown: {instance.DebugId}" );

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
                    Logging.LogTransport( $"TransportProvider: Loopback {data}" );
                    TransportProvider.Inst.DistributeIncomingMessage( null, data.CreateHeader16 );
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
                        if ( transp == null )
                        {
                            throw new FailedToConnectException( $"Unable to contact {dest}" );
                        }
                        transp.Send( data );
                    }
                    else
                    {
                        if ( TransportProvider.Inst.CurrentlyUnresolvableRouters.Contains( dest ) )
                        {
                            throw new ArgumentException( $"Unable to resolve {dest}" );
                        }

                        TransportProvider.Inst.CurrentlyUnknownRouters.Add( dest, data );
                    }
                }
            }
            catch ( FailedToConnectException ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null && NetDb.Inst != null ) NetDb.Inst.Statistics.FailedToConnect( dest );
                Logging.LogTransport( $"TransportProvider.Send: {( transp == null ? "<>" : transp.DebugId )}" +
                    $" Exception {ex.GetType()}" );

                return false;
            }
            catch ( EndOfStreamEncounteredException ex )
            {
                TransportProvider.Inst.Remove( transp );

                Logging.LogTransport( $"TransportProvider.Send: Connection {( transp == null ? "<>" : transp.DebugId )}" +
                    $" closed exception: {ex.GetType()}" );

                if ( reclvl > 1 || !Send( dest, data, reclvl + 1 ) )
                {
                    Logging.LogTransport( $"TransportProvider.Send: Recconnection failed to {dest.Id32Short}, reclvl: {reclvl}." );
                    throw;
                }
            }
            catch ( RouterUnresolvableException ex )
            {
                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                Logging.LogTransport( $"TransportProvider.Send: Unresolvable router: {ex.Message}" );

                return false;
            }
            catch ( Exception ex )
            {
                if ( transp != null ) TransportProvider.Inst.Remove( transp );

                if ( dest != null ) NetDb.Inst.Statistics.DestinationInformationFaulty( dest );
                Logging.LogTransport( $"TransportProvider.Send: Exception {ex.GetType()}, {ex.Message}" );

                throw;
            }
            return true;
        }
    }
}
