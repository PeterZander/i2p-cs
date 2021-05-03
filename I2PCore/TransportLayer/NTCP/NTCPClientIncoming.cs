using System.Net.Sockets;
using System.Net;
using I2PCore.SessionLayer;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.NTCP
{
    public class NTCPClientIncoming: NTCPClient
    {
        public override IPAddress RemoteAddress { get { return ( (IPEndPoint)MySocket.RemoteEndPoint ).Address; } }

        readonly NTCPHost Host;

        public NTCPClientIncoming( NTCPHost host, Socket s ) : base( false ) 
        {
            Host = host;
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
#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "X1X +" + TransportInstance.ToString() + "+" );
#endif

            DHHandshakeContext dhcontext = new DHHandshakeContext( this );
            dhcontext.RunContext = NTCPContext;

            SessionRequest.Receive( dhcontext, BlockReceive( 288 ) );
#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "X2X +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionCreated.Send( dhcontext ) );

            SessionConfirmA.Receive( dhcontext, BlockReceiveAtLeast( 448, 2048 ) );
#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "X3X +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionConfirmB.Send( dhcontext ) );

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "X4X +" + TransportInstance.ToString() + "+" );
#endif
            NetDb.Inst.Statistics.SuccessfulConnect( NTCPContext.RemoteRouterIdentity.IdentHash );

            Host.ReportConnectionCreated( this, dhcontext.RemoteRI.IdentHash );

            NTCPContext.SessionKey = dhcontext.SessionKey;
            NTCPContext.Encryptor = dhcontext.Encryptor;
            NTCPContext.Dectryptor = dhcontext.Dectryptor;

            RouterContext.Inst.IsFirewalled = false;
        }

    }
}

