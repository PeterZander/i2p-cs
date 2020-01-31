using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Router;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace I2PCore.Transport.NTCP
{
    public class NTCPClientOutgoing: NTCPClient
    {
        I2PRouterAddress Address;

        IPAddress OutgoingAddress;
        int OutgoingPort;

        public override IPAddress RemoteAddress { get { return OutgoingAddress; } }

        public NTCPClientOutgoing( I2PRouterAddress addr, I2PKeysAndCert dest )
            : base()
        {
            Address = addr;
            NTCPContext.RemoteRouterIdentity = dest;

            RemoteDescription = Address.Options["host"];
            OutgoingAddress = addr.Host;
            OutgoingPort = int.Parse( Address.Options["port"] );
        }

        protected override Socket CreateSocket()
        {
            Socket result = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );

            try
            {
                result.Connect( OutgoingAddress, OutgoingPort );
            }
            catch ( SocketException ex )
            {
                NetDb.Inst.Statistics.FailedToConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                throw new FailedToConnectException( ex.ToString() );
            }

            Logging.LogInformation( string.Format( "NTCP +{0}+ connected to {1}",
                    TransportInstance, result.RemoteEndPoint ) );

            return result;
        }

        public override void Connect()
        {
            base.Connect();
        }

        protected override void DHNegotiate()
        {
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "1 +" + TransportInstance.ToString() + "+" );
#endif

            DHHandshakeContext dhcontext = new DHHandshakeContext( this );
            dhcontext.RemoteRI = NTCPContext.RemoteRouterIdentity;
            dhcontext.RunContext = NTCPContext;

            SendRaw( SessionRequest.Send( dhcontext ) );
            SessionCreated.Receive( dhcontext, BlockReceive( 304 ) );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "2 +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionConfirmA.Send( dhcontext ) );
            SessionConfirmB.Receive( dhcontext, NTCPContext.RemoteRouterIdentity );

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "3 +" + TransportInstance.ToString() + "+" );
#endif

            NTCPContext.SessionKey = dhcontext.SessionKey;
            NTCPContext.Encryptor = dhcontext.Encryptor;
            NTCPContext.Dectryptor = dhcontext.Dectryptor;
        }

    }
}
