using System;
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
        }

        Dictionary<Thread, MonitoredInfo> Watched = new Dictionary<Thread, MonitoredInfo>();
        Dictionary<Thread, DateTime> PingQueue = new Dictionary<Thread, DateTime>();

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

                        Thread[] pings;

                        lock ( PingQueue )
                        {
                            pings = PingQueue.Select( p => p.Key ).ToArray();
                            PingQueue.Clear();
                        }

                        lock ( Watched )
                        {
                            var now = DateTime.Now;
                            foreach ( var one in pings ) if ( Watched.ContainsKey( one ) ) Watched[one].LastCheckin = now;
                        }

                        KeyValuePair<Thread, MonitoredInfo>[] selection;
                        lock ( Watched )
                        {
                            selection = Watched.Where( mi => ( DateTime.Now - mi.Value.LastCheckin ).TotalMilliseconds > mi.Value.Timeout ).ToArray();
                        }

                        foreach ( var one in selection )
                        {
                            Logging.Log( "Watchdog. Killing thread: " + one.Key.ManagedThreadId.ToString() );
                            try
                            {
                                one.Key.Abort();
                                if ( !one.Key.Join( 300 ) )
                                {
                                    one.Key.Interrupt();
                                }

                                lock ( Watched )
                                {
                                    Watched.Remove( one.Key );
                                }
                            }
                            catch ( Exception ex )
                            {
                                Logging.Log( "Watchdog: " + ex.ToString() );
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

        public void StartMonitor( Thread thread, int timeoutms )
        {
            Logging.Log( "Watchdog. Start monitoring thread: " + thread.ManagedThreadId.ToString() );
            lock ( Watched )
            {
                if ( Watched.ContainsKey( thread ) ) throw new InvalidOperationException( "Thread already watched!" );
                Watched[thread] = new MonitoredInfo() { Timeout = timeoutms, LastCheckin = DateTime.Now };
            }
        }

        public void StopMonitor( Thread thread )
        {
            Logging.Log( "Watchdog. Stop monitoring thread: " + thread.ManagedThreadId.ToString() );
            lock ( Watched )
            {
                Watched.Remove( thread );
            }
        }

        public void UpdateTimeout( Thread thread, int timeoutms )
        {
            lock ( Watched )
            {
                if ( !Watched.ContainsKey( thread ) ) throw new InvalidOperationException( "Thread not watched!" );
                var mi = Watched[thread];
                mi.Timeout = timeoutms;
                mi.LastCheckin = DateTime.Now;
            }
        }

        public void Ping( Thread thread )
        {
            lock ( PingQueue ) PingQueue[thread] = DateTime.MinValue;
        }
    }
}
