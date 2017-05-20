using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Router;
using I2PCore.Data;
using System.Net.Sockets;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.Transport.NTCP
{
    public class NTCPClientIncoming: NTCPClient
    {
        public override IPAddress RemoteAddress { get { return ( (IPEndPoint)MySocket.RemoteEndPoint ).Address; } }

        public NTCPClientIncoming( Socket s ) : base() 
        {
            MySocket = s;
        }

        public override void Connect()
        {
            base.Connect();
        }

        protected override Socket CreateSocket()
        {
            return MySocket;
        }

        protected override void DHNegotiate()
        {
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "X1X +" + TransportInstance.ToString() + "+" );
#endif

            DHHandshakeContext dhcontext = new DHHandshakeContext( this );
            dhcontext.RunContext = NTCPContext;

            SessionRequest.Receive( dhcontext, BlockReceive( 288 ) );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "X2X +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionCreated.Send( dhcontext ) );

            SessionConfirmA.Receive( dhcontext, BlockReceiveAtLeast( 448, 2048 ) );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "X3X +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionConfirmB.Send( dhcontext ) );

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "X4X +" + TransportInstance.ToString() + "+" );
#endif
            NetDb.Inst.Statistics.SuccessfulConnect( NTCPContext.RemoteRouterIdentity.IdentHash );

            NTCPContext.SessionKey = dhcontext.SessionKey;
            NTCPContext.Encryptor = dhcontext.Encryptor;
            NTCPContext.Dectryptor = dhcontext.Dectryptor;
        }

    }
}

