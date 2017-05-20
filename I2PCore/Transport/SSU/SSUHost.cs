using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using I2PCore.Utils;
using System.Threading;
using I2PCore.Data;
using I2PCore.Router;
using I2PCore.Transport.SSU.Data;
using System.Diagnostics;

namespace I2PCore.Transport.SSU
{
    public class SSUHost
    {
        Thread Worker;
        public bool Terminated { get; protected set; }

        public event Action<ITransport> ConnectionCreated;

        public static readonly bool PeerTestSupported = true;
        public static readonly bool IntroductionSupported = false;

        internal delegate void RelayResponseInfo( SSUHeader header, RelayResponse response, IPEndPoint ep );
        object RelayResponseReceivedLock = new object();
        internal event RelayResponseInfo RelayResponseReceived;

#if DEBUG
        const int SessionCallWarningLevelMilliseconds = 450;
#endif

        RouterContext MyRouterContext;
        IMTUProvider MTUProvider;
        HashSet<IPAddress> OurIPs;

        public SSUHost( RouterContext rc, IMTUProvider mtup )
        {
            MyRouterContext = rc;
            MTUProvider = mtup;
            MyRouterContext.NetworkSettingsChanged += new Action( NetworkSettingsChanged );

            OurIPs = new HashSet<IPAddress>( Dns.GetHostEntry( Dns.GetHostName() ).AddressList );

            Worker = new Thread( () => Run() );
            Worker.Name = "SSUHost";
            Worker.IsBackground = true;
            Worker.Start();
        }

        Dictionary<IPEndPoint, SSUSession> Sessions = new Dictionary<IPEndPoint, SSUSession>( new EPComparer() );
        List<SSUSession> NeedsCpu = new List<SSUSession>();

        Socket MySocket;
        EndPoint RemoteEP;
        EndPoint LocalEP;

        byte[] ReceiveBuf = new byte[65536];
        internal SendBufferPool SendBuffers = new SendBufferPool();

        #region Monitoring, Management

        HashSet<SSUSession> FailedSessions = new HashSet<SSUSession>();
        void AddFailedSession( SSUSession sess )
        {
            if ( sess == null ) return;

            sess.Terminate();

            lock( FailedSessions )
            {
                FailedSessions.Add( sess );
            }
        }

        SSUSession PopFailedSession()
        {
            lock ( FailedSessions )
            {
                var result = FailedSessions.FirstOrDefault();
                if ( result == null ) return null;
                FailedSessions.Remove( result );
                return result;
            }
        }

        public void Terminate()
        {
            Terminated = true;
        }

        void Run()
        {
            try
            {
                CreateSocket();

                while ( !Terminated )
                {
                    try
                    {
                        MySocket.BeginReceiveFrom( ReceiveBuf, 0, ReceiveBuf.Length, SocketFlags.None, ref RemoteEP,
                            new AsyncCallback( ReceiveCallback ), MySocket );

                        while ( !Terminated )
                        {
                            Thread.Sleep( 50 );

                            SSUSession[] sessions;
                            lock ( NeedsCpu )
                            {
                                sessions = NeedsCpu.ToArray();
                            }

                            if ( sessions.Length > 0 )
                            {
                                RunBatchWait batchsync = new RunBatchWait( sessions.Length );
                                foreach ( var sess in sessions ) ThreadPool.QueueUserWorkItem( cb => RunSession( sess, batchsync ) );
                                if ( !batchsync.WaitOne( 5000 ) )
                                {
                                    DebugUtils.LogDebug( "SSUHost: Run tasks counting error." );
                                }
                            }

                            if ( FailedSessions.Count > 0 )
                            {
                                SSUSession sess;
                                while ( ( sess = PopFailedSession() ) != null )
                                {
                                    DebugUtils.LogDebug( "SSUHost: Failed Session " + sess.DebugId + " removed." );

                                    if ( sess.RemoteEP != null ) ReportEPProblem( sess.RemoteEP );
                                    RemoveSession( sess );
                                    sess.Terminate();
                                }
                            }
                        }
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
                Worker = null;
            }
        }

        private void RunSession( SSUSession sess, RunBatchWait sync )
        {
            if ( sess.Terminated ) return;

            try
            {
                lock ( sess )
                {
#if DEBUG
                    Stopwatch Stopwatch1 = new Stopwatch();
                    Stopwatch1.Start();
#endif
                    var running = sess.Run();
                    if ( !running )
                    {
                        DebugUtils.LogDebug( "SSUHost: Terminated Session " + sess.DebugId + " removed." );
                        RemoveSession( sess );
                    }
#if DEBUG
                    Stopwatch1.Stop();
                    if ( Stopwatch1.ElapsedMilliseconds > SessionCallWarningLevelMilliseconds )
                    {
                        DebugUtils.LogDebug( () =>
                            string.Format( "SSUHost Run: WARNING Session {0} used {1}ms cpu.", sess, Stopwatch1.ElapsedMilliseconds ) );
                    }
#endif
                }
            }
            catch ( ThreadAbortException taex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( taex );
            }
            catch ( ThreadInterruptedException tiex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( tiex );
            }
            catch ( ChecksumFailureException cfex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( cfex );
            }
            catch ( SignatureCheckFailureException scex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( scex );
            }
            catch ( EndOfStreamEncounteredException eosex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( eosex );
            }
            catch ( FailedToConnectException fcex )
            {
                AddFailedSession( sess );
                DebugUtils.LogDebug( () =>
                    string.Format( "SSUHost Run: Session failed to connect: {0}", fcex.Message ) );

                if ( sess != null && sess.RemoteRouterIdentity != null )
                {
                    NetDb.Inst.Statistics.FailedToConnect( sess.RemoteRouterIdentity.IdentHash );
                }

                // Reserve the execption list for serious errors
                // sess.RaiseException( fcex );
            }
            catch ( Exception ex )
            {
                AddFailedSession( sess );
                DebugUtils.Log( ex );

                sess.RaiseException( ex );
            }
            finally
            {
                sync.Set();
            }
        }

        public void NetworkSettingsChanged()
        {
            MySocket.Close( 1 );

            CreateSocket();
        }

        private void CreateSocket()
        {
            IPAddress local = MyRouterContext.Address;
            LocalEP = new IPEndPoint( local, MyRouterContext.UDPPort );
            RemoteEP = LocalEP;

            var newsocket = new Socket( AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp );
            newsocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 65536 );
            newsocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 65536 );
            newsocket.Bind( LocalEP );

            var oldsocket = MySocket;
            MySocket = newsocket;
            if ( oldsocket != null ) oldsocket.Close();

            DebugUtils.LogInformation( "SSUHost: Running with new network settings. " +
                local.ToString() + ":" + MyRouterContext.UDPPort.ToString() + " (" + MyRouterContext.ExtAddress.ToString() + ")" );
        }

        internal void NeedCpu( SSUSession sess )
        {
            lock ( NeedsCpu )
            {
                if ( NeedsCpu.Contains( sess ) ) return;
                NeedsCpu.Add( sess );
            }
        }

        internal void NoCpu( SSUSession sess )
        {
            lock ( NeedsCpu )
            {
                if ( !NeedsCpu.Contains( sess ) ) return;
                NeedsCpu.Remove( sess );
            }
        }

        #endregion

        #region Send / Receive
        private void ReceiveCallback( IAsyncResult ar )
        {
            SSUSession session = null;
            try
            {
                EndPoint ep = RemoteEP;
                var size = MySocket.EndReceiveFrom( ar, ref ep );

                if ( ep.AddressFamily != AddressFamily.InterNetwork ) return; // TODO: Add IPV6

                if ( size <= 37 )
                {
#if LOG_ALL_TRANSPORT
                    DebugUtils.Log( string.Format( "SSU Recv: {0} bytes [0x{0:X}] from {1} (hole punch, ignored)", size, ep ) );
#endif
                    return;
                }

                var key = (IPEndPoint)ep;

#if LOG_ALL_TRANSPORT
                DebugUtils.Log( string.Format( "SSU Recv: {0} bytes [0x{0:X}] from {1}", size, ep ) );
#endif

                lock ( Sessions )
                {
                    if ( !Sessions.ContainsKey( key ) )
                    {
                        if ( IPFilter.IsFiltered( ( (IPEndPoint)ep ).Address ) )
                        {
                            DebugUtils.LogDebug( () => string.Format( "SSUHost ReceiveCallback: IPAddress {0} is blocked. {1} bytes.",
                                key, size ) );
                            return;
                        }

                        session = new SSUSession( this, (IPEndPoint)ep, MTUProvider, MyRouterContext );
                        Sessions[key] = session;
                        DebugUtils.LogDebug( "SSUHost: incoming connection " + session.DebugId + " from " + key.ToString() + " created." );
                        NeedCpu( session );
                        if ( ConnectionCreated != null ) ConnectionCreated( session );
                    }
                    else
                    {
                        session = Sessions[key];
                    }
                }

                var localbuffer = BufRefLen.Clone( ReceiveBuf, 0, size );

#if DEBUG
                Stopwatch Stopwatch1 = new Stopwatch();
                Stopwatch1.Start();
#endif
                try
                {
                    session.Receive( localbuffer );
                }
                catch ( ThreadAbortException taex )
                {
                    AddFailedSession( session );
                    DebugUtils.Log( taex );
                }
                catch ( ThreadInterruptedException tiex )
                {
                    AddFailedSession( session );
                    DebugUtils.Log( tiex );
                }
                catch ( ChecksumFailureException cfex )
                {
                    AddFailedSession( session );
                    DebugUtils.Log( cfex );
                }
                catch ( SignatureCheckFailureException scex )
                {
                    AddFailedSession( session );
                    DebugUtils.Log( scex );
                }
                catch ( FailedToConnectException fcex )
                {
                    AddFailedSession( session );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( fcex );
#endif
                    if ( session != null )
                    {
                        NetDb.Inst.Statistics.FailedToConnect( session.RemoteRouterIdentity.IdentHash );
                        session.RaiseException( fcex );
                    }
                }
#if DEBUG
                Stopwatch1.Stop();
                if ( Stopwatch1.ElapsedMilliseconds > SessionCallWarningLevelMilliseconds )
                {
                    DebugUtils.LogDebug( () =>
                        string.Format( "SSUHost ReceiveCallback: WARNING Session {0} used {1}ms cpu.", session, Stopwatch1.ElapsedMilliseconds ) );
                }
#endif
            }
            catch ( Exception ex )
            {
                AddFailedSession( session );
                DebugUtils.Log( ex );

                if ( session != null && session.RemoteRouterIdentity != null && NetDb.Inst != null )
                {
                    NetDb.Inst.Statistics.DestinationInformationFaulty( session.RemoteRouterIdentity.IdentHash );
                    session.RaiseException( ex );
                }
            }
            finally
            {
                RemoteEP = LocalEP;
                MySocket.BeginReceiveFrom( ReceiveBuf, 0, ReceiveBuf.Length, SocketFlags.None, ref RemoteEP,
                    new AsyncCallback( ReceiveCallback ), MySocket );
            }
        }

        internal void Send( IPEndPoint ep, BufLen data )
        {
            MySocket.BeginSendTo( data.BaseArray, data.BaseArrayOffset, data.Length, SocketFlags.None, ep, new AsyncCallback( SendCallback ), data );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( string.Format( "SSU Sent: {0} bytes [0x{0:X}] to {1}", data.Length, ep ) );
#endif
        }

        private void SendCallback( IAsyncResult ar )
        {
            try
            {
                MySocket.EndSendTo( ar );
            }
            catch ( Exception ex )
            {
                DebugUtils.Log( ex );
            }
            finally
            {
                SendBuffers.Push( (BufLen)ar.AsyncState );
            }
        }
        #endregion

        bool AllowConnectToSelf { get; set; }

        #region Session mgmt
        public ITransport AddSession( I2PRouterAddress addr, I2PKeysAndCert dest )
        {
            IPEndPoint remoteep = null;
            IPEndPoint key = null;

            if ( addr.HaveHostAndPort )
            {
                remoteep = new IPEndPoint( addr.Host, addr.Port );

                if ( !AllowConnectToSelf && IsOurIP( remoteep.Address ) )
                {
                    DebugUtils.Log( string.Format( "SSU AddSession: [{0}]:{1} - {2}. Dropped. Not connecting to ourselves.", dest.IdentHash.Id32, key, addr ) );
                    return null;
                }

                key = remoteep;

#if LOG_ALL_TRANSPORT
                DebugUtils.Log( string.Format( "SSU AddSession: [{0}]:{1} - {2}", dest.IdentHash.Id32, key, addr ) );
#endif

                lock ( Sessions )
                {
                    if ( Sessions.ContainsKey( key ) )
                    {
                        var sess = Sessions[key];
                        return sess;
                    }
                }
            }

            var newsess = new SSUSession( this, remoteep, addr, dest, MTUProvider, MyRouterContext );
            if ( key != null ) lock ( Sessions )
            {
                Sessions[key] = newsess;
            }
            return newsess;
        }

        internal bool IsOurIP( IPAddress addr )
        {
            lock ( OurIPs )
            {
                return OurIPs.Contains( addr );
            }
        }

        private void RemoveSession( SSUSession sess )
        {
            lock ( NeedsCpu )
            {
                if ( NeedsCpu.Contains( sess ) ) NeedsCpu.Remove( sess );
            }

            lock ( Sessions )
            {
                var key = Sessions.Where( s => s.Value == sess ).Select( s => s.Key ).SingleOrDefault();
                if ( key != null ) Sessions.Remove( key );
            }
        }

        #endregion

        #region SSU utils
        static readonly DateTime SSURefDateTime = new DateTime( 1970, 1, 1 );
        public static uint SSUTime( DateTime dt ) { return (uint)( ( dt - SSURefDateTime ).TotalSeconds ); }
        public static DateTime SSUDateTime( uint sec ) { return SSURefDateTime.AddSeconds( sec ); }
        #endregion

        class EPComparer : IEqualityComparer<IPEndPoint>
        {
            public bool Equals( IPEndPoint x, IPEndPoint y )
            {
                if ( x == null && y == null ) return false;
                if ( x == null || y == null ) return false;
                if ( ReferenceEquals( x, y ) ) return true;
                return ( BufUtils.Equal( x.Address.GetAddressBytes(), y.Address.GetAddressBytes() ) && x.Port == y.Port );
            }

            public int GetHashCode( IPEndPoint obj )
            {
                return obj.Address.GetAddressBytes().ComputeHash() ^ obj.Port;
            }
        }


        LinkedList<IPAddress> ReportedAddresses = new LinkedList<IPAddress>();
        TickCounter LastIPReport = TickCounter.MaxDelta;
        TickCounter LastExternalIPProcess = TickCounter.MaxDelta;

        internal void ReportedAddress( IPAddress ipaddr )
        {
            DebugUtils.LogDebug( "SSU My external IP " + ipaddr.ToString() );

            if ( LastExternalIPProcess.DeltaToNowMilliseconds < 20000 ) return;
            LastExternalIPProcess.SetNow();

            lock ( ReportedAddresses )
            {
                ReportedAddresses.AddLast( ipaddr );
                while ( ReportedAddresses.Count > 50 ) ReportedAddresses.RemoveFirst();

                var first = ReportedAddresses.First.Value;
                var firstbytes = first.GetAddressBytes();
                if ( ReportedAddresses.Count() > 10 && ReportedAddresses.All( a => BufUtils.Equal( a.GetAddressBytes(), firstbytes ) ) )
                {
                    DebugUtils.Log( "SSU Start using unanimous remote reported external IP " + ipaddr.ToString() );
                    UpdateSSUReportedAddr( ipaddr );
                }
                else
                {
                    var freq = ReportedAddresses.GroupBy( a => a.GetAddressBytes() ).OrderBy( g => g.Count() );
                    if ( freq.First().Count() > 15 )
                    {
                        DebugUtils.Log( "SSU Start using most frequently reported remote external IP " + ipaddr.ToString() );
                        UpdateSSUReportedAddr( ipaddr );
                    }
                }
            }
        }

        private void UpdateSSUReportedAddr( IPAddress ipaddr )
        {
            if ( LastIPReport.DeltaToNow.ToMinutes < 30 ) return;
            LastIPReport.SetNow();
            MyRouterContext.SSUReportedAddr( ipaddr );
        }

        DecayingIPBlockFilter IPFilter = new DecayingIPBlockFilter();
        public int BlockedIPCount { get { return IPFilter.Count; } }

        void ReportEPProblem( IPEndPoint ep )
        {
            IPFilter.ReportProblem( ep.Address );
        }

        internal void ReportRelayResponse( SSUHeader header, RelayResponse response, IPEndPoint ep )
        {
            if ( RelayResponseReceived != null )
            {
                lock ( RelayResponseReceivedLock )
                {
                    RelayResponseReceived( header, response, ep );
                }
            }
        }

        #region PeerTest

        internal PeerTestState PeerTestInstance = new PeerTestState();
        Dictionary<uint, PeerTestNonceInfo> KnownPeerTestNonces = new Dictionary<uint, PeerTestNonceInfo>();

        internal PeerTestNonceInfo GetNonceInfo( uint nonce )
        {
            PeerTestNonceInfo nonceinfo;

            lock ( KnownPeerTestNonces )
            {
                var remove = KnownPeerTestNonces.Where( p => p.Value.Created.DeltaToNowMilliseconds > PeerTestState.PeerTestNonceLifetimeMilliseconds ).
                    Select( p => p.Key ).ToArray();
                foreach ( var key in remove ) KnownPeerTestNonces.Remove( key );

                if ( !KnownPeerTestNonces.TryGetValue( nonce, out nonceinfo ) ) nonceinfo = null;
            }

            return nonceinfo;
        }

        internal void SetNonceInfo( uint nonce, PeerTestRole role )
        {
            lock ( KnownPeerTestNonces )
            {
                KnownPeerTestNonces[nonce] = new PeerTestNonceInfo( role );
            }
        }

        internal void SendFirstPeerTestToCharlie( PeerTest msg )
        {
            lock ( Sessions )
            {
                if ( Sessions.Count > 0 ) Sessions.Random().SendFirstPeerTestToCharlie( msg );
            }
        }

        #endregion
    }

    internal class PeerTestNonceInfo
    {
        public TickCounter Created = new TickCounter();
        public PeerTestRole Role;

        public PeerTestNonceInfo( PeerTestRole role )
        {
            Role = role;
        }
    }

}
