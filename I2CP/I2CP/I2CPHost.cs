using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Collections.Concurrent;

namespace I2P.I2CP
{
    public class I2CPHost
    {
        Thread Worker;
        bool Terminated = false;

        public const int DefaultI2CPPort = 7654;
        static bool UseIpV6 = false;

        internal ConcurrentDictionary<IPEndPoint, I2CPSession> Sessions = 
                new ConcurrentDictionary<IPEndPoint, I2CPSession>();

        public I2CPHost()
        {
            Worker = new Thread( RunHost )
            {
                Name = "NTCPHost",
                IsBackground = true
            };
            Worker.Start();
        }

        void RunHost()
        {
            try
            {
                while ( !Terminated )
                {
                    var listener = new TcpListener( IPAddress.Any, DefaultI2CPPort );
                    listener.Start();

                    try
                    {
                        listener.BeginAcceptTcpClient( HandleListenerAsyncCallback, listener );

                        while ( !Terminated )
                        {
                            Thread.Sleep( 10000 );

                            var stuck = Sessions.Where( s =>
                                    s.Value.LastReception.DeltaToNow > TickSpan.Minutes( 30 ) );

                            foreach ( var one in stuck )
                            {
                                try
                                {
                                    Logging.LogWarning( $"{this}: Terminating stuck session {one} {one.Value.LastReception}" );
                                    one.Value.Terminate();
                                }
                                catch ( Exception ex )
                                {
                                    Logging.Log( ex );
                                }
                            }

                            var terminated = Sessions.Where( s =>
                                    s.Value.CurrentState is null );

                            foreach ( var one in terminated )
                            {
                                try
                                {
                                    Logging.LogInformation( $"{this}: Removing terminated session {one}" );
                                    Sessions.TryRemove( one.Key, out _ );
                                }
                                catch ( Exception ex )
                                {
                                    Logging.Log( ex );
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
                    finally 
                    {
                        listener.Stop();
                    }
                }
            }
            finally
            {
                Terminated = true;
                Worker = null;
            }
        }

        void HandleListenerAsyncCallback( IAsyncResult ar )
        {
            if ( !ar.IsCompleted ) return;

            var listener = (TcpListener)ar.AsyncState;
            var tcpclient = listener.EndAcceptTcpClient( ar );

            var i2cpc = new I2CPSession( this, tcpclient );
            Logging.LogInformation( $"{this}: incoming connection ${i2cpc.DebugId} from {tcpclient.Client.RemoteEndPoint} created." );

            Sessions[(IPEndPoint)tcpclient.Client.RemoteEndPoint] = i2cpc;

            _ = i2cpc.Run();

            listener.BeginAcceptTcpClient( HandleListenerAsyncCallback, listener );
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
