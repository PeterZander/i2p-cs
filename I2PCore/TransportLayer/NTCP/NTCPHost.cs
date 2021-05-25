using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using I2PCore.Data;

namespace I2PCore.TransportLayer.NTCP
{
    [TransportProtocol]
    public class NTCPHost: ITransportProtocol
    {
        Thread Worker;
        readonly CancellationTokenSource MyCancellationTokenSource;
        readonly CancellationToken MyCancellationToken;

        public event Action<ITransport,I2PIdentHash> ConnectionCreated;

        List<NTCPClientIncoming> Clients = new List<NTCPClientIncoming>();

        public NTCPHost()
        {
            MyCancellationTokenSource = new CancellationTokenSource();
            MyCancellationToken = MyCancellationTokenSource.Token;

            RouterContext.Inst.NetworkSettingsChanged += NetworkSettingsChanged;

            UpdateRouterContext();

            Worker = new Thread( Run )
            {
                Name = "NTCPHost",
                IsBackground = true
            };
            Worker.Start();
        }

        void Run()
        {
            try
            {
                while ( !MyCancellationToken.IsCancellationRequested )
                {
                    var listener = CreateListener();

                    try
                    {
                        listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );

                        while ( !MyCancellationToken.IsCancellationRequested )
                        {
                            Thread.Sleep( 2000 );

                            lock ( Clients )
                            {
                                var terminated = Clients.Where( c => ( (ITransport)c ).IsTerminated ).ToArray();
                                foreach ( var one in terminated )
                                {
                                    Clients.Remove( one );
                                }
                            }

                            if ( SettingsChanged )
                            {
                                SettingsChanged = false;

                                CloseListener( listener );

                                Thread.Sleep( 3000 );

                                listener = CreateListener();
                                listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );

                                Logging.LogInformation( $"NTCPHost: Running with new network settings. " +
                                    $"{listener.LocalEndPoint}:{RouterContext.Inst.TCPPort}" + 
                                    $" ({RouterContext.Inst.ExtIPV4Address})" );
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

                    CloseListener( listener );
                }
            }
            finally
            {
                Worker = null;
            }
        }

        private static Socket CreateListener()
        {
            Socket listener;
            listener = new Socket( RouterContext.Inst.LocalInterface.AddressFamily, SocketType.Stream, ProtocolType.Tcp );
            if ( RouterContext.UseIpV6 ) listener.DualMode = true;
            listener.Bind( new IPEndPoint( RouterContext.Inst.LocalInterface, RouterContext.Inst.TCPPort ) );
            listener.Listen( 20 );
            return listener;
        }
        private static void CloseListener( Socket listener )
        {
            listener.Shutdown( SocketShutdown.Both );
            listener.Close();
        }

        bool SettingsChanged = false;

        public int BlockedRemoteAddressesCount => 0;

        public void NetworkSettingsChanged()
        {
            SettingsChanged = true;
            UpdateRouterContext();
        }

        private void UpdateRouterContext()
        {
            if ( RouterContext.Inst.IsFirewalled )
            {
                RouterContext.Inst.UpdateAddress( this, null );
            }
            else
            {
                var addrs = new List<I2PRouterAddress>();

                if ( RouterContext.UseIpV4 )
                {
                    var addr4 = new I2PRouterAddress( RouterContext.Inst.ExtIPV4Address, RouterContext.Inst.TCPPort, 11, "NTCP" );
                    addrs.Add( addr4 );
                }

                if ( RouterContext.UseIpV6 )
                {
                    var lv6 = SSU.SSUHost.GetLocalIpV6Address();

                    var addr6 = new I2PRouterAddress( lv6, RouterContext.Inst.TCPPort, 11, "NTCP" );
                    addrs.Add( addr6 );
                }

                RouterContext.Inst.UpdateAddress( this, addrs );
            }
        }

        void DoAcceptTcpClientCallback( IAsyncResult ar )
        {
            if ( !ar.IsCompleted ) return;
            bool docontinue = true;

            var listener = (Socket)ar.AsyncState;

            try
            {
                var socket = listener.EndAccept( ar );

                var ntcpc = new NTCPClientIncoming( this, socket );
                Logging.LogTransport( $"NTCPHost: incoming connection {ntcpc.DebugId} from " + 
                    $"{socket.RemoteEndPoint} created." );

                ntcpc.Connect();
                lock ( Clients )
                {
                    Clients.Add( ntcpc );
                }
            }
            catch ( ObjectDisposedException )
            {
                docontinue = false;
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }

            try
            {
                if ( docontinue ) listener.BeginAccept( new AsyncCallback( DoAcceptTcpClientCallback ), listener );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
        }

        internal void ReportConnectionCreated( ITransport transport, I2PIdentHash routerid )
        {
#if DEBUG
            if ( ConnectionCreated == null )
                    Logging.LogWarning( "NTCPHost: No observers for ConnectionCreated!" );
#endif
            ConnectionCreated?.Invoke( transport, routerid );
        }

        public ProtocolCapabilities ContactCapability( I2PRouterInfo router )
        {
            return router.Adresses.Any( ra => ra.TransportStyle == "NTCP" && ra.HaveHostAndPort )
                            ? ProtocolCapabilities.IncomingLowPrio
                            : ProtocolCapabilities.None;
        }

        public ITransport AddSession( I2PRouterInfo router )
        {
            return new NTCPClientOutgoing( router );
        }
    }
}
