using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public partial class SSUHost
    {
        Socket MySocket;
        EndPoint RemoteEP;
        EndPoint LocalEP;

        byte[] ReceiveBuf = new byte[65536];
        internal SendBufferPool SendBuffers = new SendBufferPool();

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

            var newsocket = new Socket( local.AddressFamily, SocketType.Dgram, ProtocolType.Udp );
            newsocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 65536 );
            newsocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 65536 );
            newsocket.Bind( LocalEP );

            var oldsocket = MySocket;
            MySocket = newsocket;
            if ( oldsocket != null ) oldsocket.Close();

            Logging.LogInformation( $"SSUHost: Running with new network settings. " +
                $"{local}:{MyRouterContext.UDPPort} ({MyRouterContext.ExtAddress})" );
        }

        private void ReceiveCallback( IAsyncResult ar )
        {
            SSUSession session = null;
            try
            {
                EndPoint ep = RemoteEP;
                var size = MySocket.EndReceiveFrom( ar, ref ep );

                if ( ep.AddressFamily != AddressFamily.InterNetwork
                    && ( !Router.RouterContext.Inst.UseIpV6 
                        || ep.AddressFamily != AddressFamily.InterNetworkV6 ) )
                            return; // TODO: Add IPV6

                if ( size <= 37 )
                {
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( string.Format( "SSU Recv: {0} bytes [0x{0:X}] from {1} (hole punch, ignored)", size, ep ) );
#endif
                    return;
                }

                var sessionendpoint = (IPEndPoint)ep;

#if LOG_ALL_TRANSPORT
                Logging.LogTransport( string.Format( "SSU Recv: {0} bytes [0x{0:X}] from {1}", size, ep ) );
#endif

                lock ( Sessions )
                {
                    if ( !Sessions.ContainsKey( sessionendpoint ) )
                    {
                        if ( IPFilter.IsFiltered( ( (IPEndPoint)ep ).Address ) )
                        {
                            Logging.LogTransport( $"SSUHost ReceiveCallback: IPAddress {sessionendpoint} is blocked. {size} bytes." );
                            return;
                        }

                        ++IncommingConnectionAttempts;

                        session = new SSUSession( this, (IPEndPoint)ep, MTUProvider, MyRouterContext );
                        Sessions[sessionendpoint] = session;
                        Logging.LogTransport( $"SSUHost: incoming connection " +
                            $"{session.DebugId} from {sessionendpoint} created." );
                        NeedCpu( session );
                        ConnectionCreated?.Invoke( session );
                    }
                    else
                    {
                        session = Sessions[sessionendpoint];
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
                    Logging.Log( taex );
                }
                catch ( ThreadInterruptedException tiex )
                {
                    AddFailedSession( session );
                    Logging.Log( tiex );
                }
                catch ( ChecksumFailureException cfex )
                {
                    AddFailedSession( session );
                    Logging.Log( cfex );
                }
                catch ( SignatureCheckFailureException scex )
                {
                    AddFailedSession( session );
                    Logging.Log( scex );
                }
                catch ( FailedToConnectException fcex )
                {
                    AddFailedSession( session );
#if LOG_ALL_TRANSPORT
            Logging.LogTransport( fcex );
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
                    Logging.LogTransport(
                        $"SSUHost ReceiveCallback: WARNING Session {session} used {Stopwatch1.ElapsedMilliseconds}ms cpu." );
                }
#endif
            }
            catch ( Exception ex )
            {
                AddFailedSession( session );
                Logging.Log( ex );

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
            MySocket.BeginSendTo( 
                    data.BaseArray, data.BaseArrayOffset, data.Length, 
                    SocketFlags.None, ep, 
                    new AsyncCallback( SendCallback ), 
                    data );

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( $"SSU Sent: {data.Length} bytes [0x{0:X}] to {ep}" );
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
                Logging.Log( ex );
            }
            finally
            {
                SendBuffers.Push( (BufLen)ar.AsyncState );
            }
        }

        DecayingIPBlockFilter IPFilter = new DecayingIPBlockFilter();
        public int BlockedIPCount { get { return IPFilter.Count; } }

        void ReportEPProblem( IPEndPoint ep )
        {
            IPFilter.ReportProblem( ep.Address );
        }

    }
}
