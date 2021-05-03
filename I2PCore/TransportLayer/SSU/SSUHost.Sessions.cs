using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using I2PCore.Data;
using I2PCore.SessionLayer;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SSUHost
    {

        /// <summary>
        /// Running sessions, in and out, indexed on remote end point.
        /// If running in IPV6 mode, IPV4 addresses must be wrapped as an IPV6 address (::ffff:x.y.z.w).
        /// </summary>
        ConcurrentDictionary<IPEndPoint, SSUSession> Sessions = new ConcurrentDictionary<IPEndPoint, SSUSession>( new EPComparer() );
        ConcurrentDictionary<SSUSession,object> NeedsCpu = new ConcurrentDictionary<SSUSession,object>();

        public ITransport AddSession( I2PRouterInfo router )
        {
            var addr = SelectAddress( router );
            var dest = router.Identity;

            if ( addr.HaveHostAndPort )
            {
                var remoteep = new IPEndPoint( addr.Host, addr.Port );

                if ( !AllowConnectToSelf && IsOurIP( remoteep.Address ) )
                {
                    Logging.LogTransport( $"SSU AddSession: [{dest.IdentHash.Id32}]: {addr}. Dropped. Not connecting to ourselves." );
                    return null;
                }

                var key = RouterContext.UseIpV6 
                        && remoteep.AddressFamily == AddressFamily.InterNetwork
                            ? new IPEndPoint( remoteep.Address.MapToIPv6(), remoteep.Port )
                            : remoteep;

                Logging.LogDebugData( $"SSU AddSession: [{dest.IdentHash.Id32}]:{key} - {addr}" );

                if ( Sessions.TryGetValue( key, out var session ) )
                {
                    return session;
                }
            }

            var newsession = new SSUSession( 
                    this,
                    Send,
                    router,
                    MyRouterContext );

            NeedCpu( newsession );
            return newsession;
        }

        internal I2PRouterAddress SelectAddress( I2PRouterInfo router )
        {
            var addrs = router.Adresses.Where( a => ( a.TransportStyle == "SSU" )
                        && a.Options.Contains( "key" ) );

            I2PRouterAddress addr = RouterContext.UseIpV6
                    ? addrs.FirstOrDefault( a => a.Options.ValueContains( "host", ":" ) )
                    : null;

            addr = addr is null ? addrs.FirstOrDefault( a => a.Options.ValueContains( "host", "." ) ) : addr;
            addr = addr is null ? addrs.FirstOrDefault( a => a.HaveHostAndPort ) : addr;
            addr = addr is null ? addrs.FirstOrDefault( a => a.Options.ValueContains( "ihost0", "." ) ) : addr;

            return addr;
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
            var cpuremoved = NeedsCpu.TryRemove( sess, out var _ );

            var sessions = Sessions
                .Where( s => s.Value.Equals( sess ) )
                .Select( s => s.Key );

            var scount = 0;

            foreach( var one in sessions )
            {
                Sessions.TryRemove( one, out var _ );
                ++scount;
            }

            Logging.LogDebugData( $"SSUHost: Removing session {sess} count {scount}, cpu {cpuremoved}" );
        }

        internal bool SessionEndPointUpdated( SSUSession sess, IPEndPoint newep )
        {
            var epknown = Sessions.TryGetValue( newep, out var runingsess );
            if ( epknown && runingsess == sess ) return true;

            var key = Sessions
                .Where( s => s.Value.Equals( sess ) )
                .Select( s => s.Key )
                .FirstOrDefault();

            if ( key != null ) Sessions.TryRemove( key, out var _ );

            if ( newep != null )
            {
                if ( epknown )
                {
                    return false;
                }
                else
                {
                    Sessions[newep] = sess;
                    return true;
                }
            }

            return false;
        }

        HashSet<SSUSession> FailedSessions = new HashSet<SSUSession>();
        void AddFailedSession( SSUSession sess )
        {
            if ( sess == null ) return;

            sess.Terminate();

            lock ( FailedSessions )
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

        internal bool AccessSession( IPEndPoint ep, Action<SSUSession> action )
        {
            if ( Sessions.TryGetValue( ep, out var session ) )
            {
                action( session );
                return true;
            }
            return false;
        }

        internal IEnumerable<SSUSession> FindSession( Func<SSUSession, bool> filter )
        {
            return Sessions
                .Where( p => filter( p.Value ) )
                .Select( p => p.Value )
                .ToArray();
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
                            Thread.Sleep( 300 );

                            var sessions = NeedsCpu.Keys.ToArray();

                            if ( sessions.Length > 0 )
                            {
                                var batchsync = new RunBatchWait( sessions.Length );
                                foreach ( var sess in sessions )
                                {
                                    if ( !ThreadPool.QueueUserWorkItem( o => 
                                    {
                                        try
                                        {
                                            RunSession( sess );
                                        }
                                        catch( Exception ex )
                                        {
                                            Logging.LogDebug( $"SSUHost: RunSession exception {ex}" );
                                        }
                                        finally
                                        {
                                            batchsync.Set();
                                        }
                                    }, sess ) )
                                    {
                                        Logging.LogDebug( "SSUHost: Run tasks QueueUserWorkItem failed." );
                                        batchsync.Set();
                                    }
                                }
                                if ( !batchsync.WaitOne( 1500 ) )
                                {
                                    Logging.LogWarning( "SSUHost: Run tasks counting error." );
                                }
                            }

                            if ( FailedSessions.Count > 0 )
                            {
                                SSUSession sess;
                                while ( ( sess = PopFailedSession() ) != null )
                                {
                                    Logging.LogTransport( $"SSUHost: Failed Session {sess.DebugId} removed." );

                                    if ( sess.RemoteEP != null ) ReportEPProblem( sess.RemoteEP );
                                    sess.Terminate();
                                    RemoveSession( sess );
                                }
                            }
                        }
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
                Worker = null;
            }
        }

        private void RunSession( SSUSession sess )
        {
            try
            {
                if ( sess.IsTerminated )
                {
                    Logging.LogTransport( $"SSUHost: RunSession {sess.DebugId} is terminated." );
                    RemoveSession( sess );
                    return;
                }

#if DEBUG
                Stopwatch Stopwatch1 = new Stopwatch();
                Stopwatch1.Start();
#endif

                bool taken = false;
                try
                {
                    Monitor.TryEnter( sess.RunLock, 200, ref taken );

                    if ( taken )
                    {
                        var running = sess.Run();

                        if ( !running )
                        {
                            Logging.LogTransport( $"SSUHost: Terminated Session {sess.DebugId} removed." );
                            sess.Terminate();
                            RemoveSession( sess );
                        }
                    }
                    else
                    {
                        Logging.LogDebug( $"SSUHost RunSession: Failed to lock {sess.DebugId} for access" );
                    }
                }
                finally
                {
                    if ( taken ) Monitor.Exit( sess.RunLock );
                }
#if DEBUG
                Stopwatch1.Stop();
                if ( Stopwatch1.ElapsedMilliseconds > SessionCallWarningLevelMilliseconds )
                {
                    Logging.LogDebug(
                        $"SSUHost Run: WARNING Session {sess} used {Stopwatch1.ElapsedMilliseconds}ms cpu." );
                }
#endif
            }
            catch ( ThreadAbortException taex )
            {
                AddFailedSession( sess );
                Logging.Log( taex );
            }
            catch ( ThreadInterruptedException tiex )
            {
                AddFailedSession( sess );
                Logging.Log( tiex );
            }
            catch ( ChecksumFailureException cfex )
            {
                AddFailedSession( sess );
                Logging.Log( cfex );
            }
            catch ( SignatureCheckFailureException scex )
            {
                AddFailedSession( sess );
                Logging.Log( scex );

                if ( sess != null && sess.RemoteRouterIdentity != null )
                {
                    NetDb.Inst.Statistics.FailedToConnect( sess.RemoteRouterIdentity.IdentHash );
                }
            }
            catch ( EndOfStreamEncounteredException eosex )
            {
                AddFailedSession( sess );
                Logging.Log( eosex );
            }
            catch ( FailedToConnectException fcex )
            {
                AddFailedSession( sess );
                Logging.LogTransport(
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
                Logging.Log( ex );

                sess.RaiseException( ex );
            }
        }

        internal void NeedCpu( SSUSession sess )
        {
            NeedsCpu[sess] = null;
        }

        internal void NoCpu( SSUSession sess )
        {
            NeedsCpu.TryRemove( sess, out var _ );
        }
    }
}
