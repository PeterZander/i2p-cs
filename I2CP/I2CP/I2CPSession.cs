using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2P.I2CP.Messages;
using I2PCore.Data;
using Org.BouncyCastle.Utilities.Encoders;
using I2PCore.Utils;
using System.Net.Sockets;
using I2P.I2CP.States;

namespace I2P.I2CP
{
    public class I2CPSession
    {
        I2CPHost Host;
        Socket MySocket;

        public ushort SessionId;
        public bool Terminated = false;

        public I2PLeaseInfo MyLeaseInfo;
        public I2PDestination MyDestination;

        public Dictionary<uint, I2PLease> Tunnels = new Dictionary<uint, I2PLease>();
        public Dictionary<uint, I2PLeaseInfo> TunnelsLeaseInfo = new Dictionary<uint, I2PLeaseInfo>();

        public string DebugId { get { return "---id---"; } }

        I2CPState CurrentState;

        public I2CPSession( SessionStatusMessage msg, I2PDestination mydest )
        {
            SessionId = msg.SessionId;

            //MyLeaseInfo = new I2PLeaseInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );
            MyDestination = mydest;
        }

        byte[] RecvBuf = new byte[8192];

        // We are host
        public I2CPSession( I2CPHost host, Socket socket )
        {
            Host = host;
            MySocket = socket;

            CurrentState = new WaitProtVer( this );
            MySocket.BeginReceive( RecvBuf, 0, RecvBuf.Length, SocketFlags.None, new AsyncCallback( ReceiveCallback ), this );
        }

        internal void Run()
        {
            if ( CurrentState == null )
            {
                Terminate();
                return;
            }

            var os = CurrentState;
            var ns = CurrentState.Run();
            if ( os != ns && ns != null ) ns = ns.Run();
            CurrentState = ns;
        }

        void ReceiveCallback( IAsyncResult ar )
        {
            try
            {
                var len = MySocket.EndReceive( ar );

                var cs = CurrentState;
                if ( cs == null ) return;

                cs.DataReceived( new BufLen( RecvBuf, 0, len ) );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
            finally
            {
                MySocket.BeginReceive( RecvBuf, 0, RecvBuf.Length, SocketFlags.None, new AsyncCallback( ReceiveCallback ), this );
            }
        }

        void Connection_ReceivedRequestVariableLeaseSetMessage( RequestVariableLeaseSetMessage msg )
        {
            /*
            var response = new CreateLeaseSetMessage(
                MyDestination,
                SessionId,
                new I2PLeaseInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ),
                msg.Leases );
            Connection.Send( response );
             */

            foreach ( var lease in msg.Leases )
            {
                Tunnels[lease.TunnelId] = lease;

                Logging.Log( lease.TunnelId.ToString() + ": " + lease.TunnelGw.Id64 );
            }
            Logging.Log( "Tunnels: " + Tunnels.Count.ToString() );
        }

        void Connection_ReceivedSessionStatusMessage( SessionStatusMessage msg )
        {
        }

        internal void Terminate()
        {
            Terminated = true;
        }

        internal void Send( I2CPMessage msg )
        {
            var data = msg.ToByteArray();
            MySocket.BeginSend( data, 0, data.Length, SocketFlags.None, new AsyncCallback( SendCompleted ), null );
        }

        void SendCompleted( IAsyncResult ar )
        {
            try
            {
                var cd = MySocket.EndSend( ar );
#if LOG_ALL_I2CP
                DebugUtils.LogDebug( string.Format( "I2CP {1} Async complete: {0} bytes [0x{0:X}]", cd, DebugId ) );
#endif
            }

            catch ( Exception ex )
            {
                Logging.Log( "I2CP SendCompleted", ex );
                Terminated = true;
            }
        }
    }
}
