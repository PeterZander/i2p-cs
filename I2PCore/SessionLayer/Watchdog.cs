using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public class Watchdog
    {
        protected static Thread Worker;
        public static Watchdog Inst = new Watchdog();

        public class MonitoredInfo
        {
            /// <summary>
            /// Milliseconds
            /// </summary>
            public int Timeout;
            public DateTime LastCheckin;
            public CancellationTokenSource CTSource;
            public readonly int DebugId;

            static int LastDebugId = 0;

            public MonitoredInfo()
            {
                DebugId = ++LastDebugId;
            }
        }

        ConcurrentDictionary<CancellationToken, MonitoredInfo> Watched = new ConcurrentDictionary<CancellationToken, MonitoredInfo>();
        ConcurrentDictionary<CancellationToken, DateTime> PingQueue = new ConcurrentDictionary<CancellationToken, DateTime>();

        protected Watchdog()
        {
            Worker = new Thread( () => Run() );
            Worker.Name = "Watchdog";
            Worker.IsBackground = true;
            Worker.Start();
        }

        bool Terminated = false;
        private void Run()
        {
            try
            {
                try
                {
                    while ( !Terminated )
                    {
                        Thread.Sleep( 2000 );

                        var pings = PingQueue
                                    .Select( p => p.Key )
                                    .ToArray();

                        PingQueue.Clear();

                        var now = DateTime.Now;
                        foreach ( var one in pings.ToArray() )
                        {
                            if ( Watched.TryGetValue( one, out var v ) ) v.LastCheckin = now;
                        }

                        var selection = Watched
                                    .Where( mi => 
                                            ( DateTime.Now - mi.Value.LastCheckin ).TotalMilliseconds > mi.Value.Timeout )
                                    .ToArray();

                        foreach ( var one in selection )
                        {
                            Logging.Log( $"Watchdog. Killing thread: {one.Value.DebugId}" );
                            try
                            {
                                if ( Watched.TryRemove( one.Key, out var v ) )
                                {
                                    v.CTSource.Cancel();
                                }
                            }
                            catch ( Exception ex )
                            {
                                Logging.Log( $"Watchdog: {ex}" );
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
            finally
            {
                Terminated = true;
            }
        }

        public CancellationToken StartMonitor( int timeoutms )
        {
            var mi = new MonitoredInfo()
            {
                Timeout = timeoutms,
                LastCheckin = DateTime.Now,
                CTSource = new CancellationTokenSource(),
            };
            var result = mi.CTSource.Token;
            Logging.Log( $"Watchdog. Start monitoring thread: {mi.DebugId}" );
            Watched.TryAdd( result, mi );

            return result;
        }

        public void StopMonitor( CancellationToken ct )
        {
            if ( !Watched.TryRemove( ct, out var mi ) ) return;
            Logging.Log( $"Watchdog. Stop monitoring thread: {mi.DebugId}" );
        }

        public void UpdateTimeout( CancellationToken ct, int timeoutms )
        {
            if ( !Watched.TryGetValue( ct, out var mi ) ) throw new InvalidOperationException( "Thread not watched!" );
            mi.Timeout = timeoutms;
            mi.LastCheckin = DateTime.Now;
        }

        public void Cancel( CancellationToken ct )
        {
            if ( !Watched.TryGetValue( ct, out var mi ) ) throw new InvalidOperationException( "Thread not watched!" );
            mi.CTSource.Cancel();
        }

        public void Ping( CancellationToken ct )
        {
            PingQueue[ct] = DateTime.MinValue;
        }
    }
}
