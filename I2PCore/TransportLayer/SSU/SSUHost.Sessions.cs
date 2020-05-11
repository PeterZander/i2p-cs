using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SSUHost
    {
        Dictionary<IPEndPoint, SSUSession> Sessions = new Dictionary<IPEndPoint, SSUSession>( new EPComparer() );
        List<SSUSession> NeedsCpu = new List<SSUSession>();

        public ITransport AddSession( I2PRouterAddress addr, I2PKeysAndCert dest )
        {
            IPEndPoint remoteep = null;
            IPEndPoint key = null;

            if ( addr.HaveHostAndPort )
            {
                remoteep = new IPEndPoint( addr.Host, addr.Port );

                if ( !AllowConnectToSelf && IsOurIP( remoteep.Address ) )
                {
                    Logging.LogTransport( $"SSU AddSession: [{dest.IdentHash.Id32}]:{key} - {addr}. Dropped. Not connecting to ourselves." );
                    return null;
                }

                key = remoteep;

                Logging.LogDebugData( $"SSU AddSession: [{dest.IdentHash.Id32}]:{key} - {addr}" );

                lock ( Sessions )
                {
                    if ( Sessions.ContainsKey( key ) )
                    {
                        var sess = Sessions[key];
                        return sess;
                    }
                }
            }

            var newsess = new SSUSession( 
                    this, 
                    Send, 
                    remoteep, 
                    addr, 
                    dest, 
                    MTUProvider, 
                    MyRouterContext );

            if ( key != null )
            {
                lock ( Sessions )
                {
                    Sessions[key] = newsess;
                }
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
                var key = Sessions
                    .Where( s => s.Value == sess )
                    .Select( s => s.Key )
                    .FirstOrDefault();

                if ( key != null ) Sessions.Remove( key );
            }
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
            lock ( Sessions )
            {
                if ( Sessions.TryGetValue( ep, out var session ) )
                {
                    action( session );
                    return true;
                }
                return false;
            }
        }

        internal IEnumerable<SSUSession> FindSession( Func<SSUSession, bool> filter )
        {
            lock ( Sessions )
            {
                return Sessions
                    .Where( p => filter( p.Value ) )
                    .Select( p => p.Value )
                    .ToArray();
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
                            Thread.Sleep( 300 );

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
                                    Logging.LogTransport( "SSUHost: Run tasks counting error." );
                                }
                            }

                            if ( FailedSessions.Count > 0 )
                            {
                                SSUSession sess;
                                while ( ( sess = PopFailedSession() ) != null )
                                {
                                    Logging.LogTransport( $"SSUHost: Failed Session {sess.DebugId} removed." );

                                    if ( sess.RemoteEP != null ) ReportEPProblem( sess.RemoteEP );
                                    RemoveSession( sess );
                                    sess.Terminate();
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
                        Logging.LogTransport( $"SSUHost: Terminated Session {sess.DebugId} removed." );
                        RemoveSession( sess );
                    }
#if DEBUG
                    Stopwatch1.Stop();
                    if ( Stopwatch1.ElapsedMilliseconds > SessionCallWarningLevelMilliseconds )
                    {
                        Logging.LogTransport(
                            $"SSUHost Run: WARNING Session {sess} used {Stopwatch1.ElapsedMilliseconds}ms cpu." );
                    }
#endif
                }
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
            finally
            {
                sync.Set();
            }
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
    }
}
