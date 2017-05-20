using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Router;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace I2P.I2CP
{
    public class I2CPHost
    {
        Thread Worker;
        bool Terminated = false;

        public const int DefaultI2CPPort = 7654;
        static bool UseIpV6 = false;

        List<I2CPSession> Sessions = new List<I2CPSession>();

        public I2CPHost()
        {
            Worker = new Thread( () => Run() );
            Worker.Name = "NTCPHost";
            Worker.IsBackground = true;
            Worker.Start();
        }

        void Run()
        {
            try
            {
                while ( !Terminated )
                {
                    var listener = CreateListener();

                    try
                    {
                        listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );

                        while ( !Terminated )
                        {
                            Thread.Sleep( 200 );

                            I2CPSession[] sessions;

                            lock ( Sessions )
                            {
                                var terminated = Sessions.Where( c => c.Terminated ).ToArray();
                                foreach ( var one in terminated )
                                {
                                    Sessions.Remove( one );
                                }

                                sessions = Sessions.ToArray();
                            }

                            foreach ( var one in sessions )
                            {
                                try
                                {
                                    one.Run();
                                }
                                catch ( Exception ex )
                                {
                                    one.Terminate();
                                    DebugUtils.Log( ex );
                                }
                            }

                            /*
                            if ( SettingsChanged )
                            {
                                SettingsChanged = false;

                                listener.Shutdown( SocketShutdown.Both );
                                listener.Close();

                                Thread.Sleep( 3000 );

                                listener = CreateListener();
                                listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );

                                DebugUtils.LogInformation( "NTCPHost: Running with new network settings. " +
                                    listener.LocalEndPoint.ToString() + ":" + RouterContext.Inst.TCPPort.ToString() + 
                                    " (" + RouterContext.Inst.ExtAddress.ToString() + ")" );
                            }
                             */
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

        private static Socket CreateListener()
        {
            Socket listener;
            IPAddress ipaddr;

            if ( UseIpV6 )
            {
                ipaddr = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.Where( a => a.AddressFamily == AddressFamily.InterNetworkV6 ).First();
            }
            else
            {
                ipaddr = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.Where( a => a.AddressFamily == AddressFamily.InterNetwork ).First();
            }

            listener = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );
            listener.Bind( new IPEndPoint( ipaddr, DefaultI2CPPort ) );
            listener.Listen( 3 );
            return listener;
        }

        /*
        bool SettingsChanged = false;

        public void NetworkSettingsChanged()
        {
            SettingsChanged = true;
        }*/

        void DoAcceptTcpClientCallback( IAsyncResult ar )
        {
            if ( !ar.IsCompleted ) return;
            bool docontinue = true;

            var listener = (Socket)ar.AsyncState;

            try
            {
                var socket = listener.EndAccept( ar );

                var i2cpc = new I2CPSession( this, socket );
                DebugUtils.LogDebug( "I2CPHost: incoming connection " + i2cpc.DebugId + " from " + socket.RemoteEndPoint.ToString() + " created." );

                lock ( Sessions )
                {
                    Sessions.Add( i2cpc );
                }
            }
            catch ( ObjectDisposedException )
            {
                docontinue = false;
            }
            catch ( Exception ex )
            {
                DebugUtils.Log( ex );
            }

            try
            {
                if ( docontinue ) listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );
            }
            catch ( Exception ex )
            {
                DebugUtils.Log( ex );
            }
        }
    }
}
