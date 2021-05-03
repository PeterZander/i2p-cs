using I2PCore.Data;
using I2PCore.Utils;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using I2PCore.SessionLayer;

namespace I2PCore.TransportLayer.NTCP
{
    public class NTCPClientOutgoing: NTCPClient
    {
        I2PRouterAddress Address;
        readonly IPAddress OutgoingAddress;
        readonly int OutgoingPort;

        public override IPAddress RemoteAddress { get { return OutgoingAddress; } }

        public NTCPClientOutgoing( I2PRouterInfo router )
            : base( true )
        {
            Address = SelectAddress( router );
            NTCPContext.RemoteRouterIdentity = router.Identity;

            RemoteDescription = Address.Options["host"];
            OutgoingAddress = Address.Host;
            OutgoingPort = int.Parse( Address.Options["port"] );
        }

        protected override Socket CreateSocket()
        {
            Socket result = new Socket( 
                        RouterContext.UseIpV6
                                ? AddressFamily.InterNetworkV6
                                : AddressFamily.InterNetwork,
                        SocketType.Stream,
                        ProtocolType.Tcp );

            if ( RouterContext.UseIpV6 ) result.DualMode = true;

            try
            {
                result.Connect( OutgoingAddress, OutgoingPort );
            }
            catch ( SocketException ex )
            {
                NetDb.Inst.Statistics.FailedToConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                throw new FailedToConnectException( ex.ToString() );
            }

            Logging.LogTransport( $"NTCP +{TransportInstance}+ connected to {result.RemoteEndPoint}" );

            return result;
        }

        public override void Connect()
        {
            base.Connect();
        }

        internal I2PRouterAddress SelectAddress( I2PRouterInfo router )
        {
            var addrs = router.Adresses.Where( a => ( a.TransportStyle == "NTCP" ) );

            I2PRouterAddress addr = RouterContext.UseIpV6
                    ? addrs.FirstOrDefault( a => a.Options.ValueContains( "host", ":" ) )
                    : null;
                    
            addr = addr is null ? addrs.FirstOrDefault( a => a.Options.ValueContains( "host", "." ) ) : addr;
            addr = addr is null ? addrs.FirstOrDefault( a => a.HaveHostAndPort ) : addr;

            return addr;
        }

        protected override void DHNegotiate()
        {
#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "1 +" + TransportInstance.ToString() + "+" );
#endif

            DHHandshakeContext dhcontext = new DHHandshakeContext( this );
            dhcontext.RemoteRI = NTCPContext.RemoteRouterIdentity;
            dhcontext.RunContext = NTCPContext;

            SendRaw( SessionRequest.Send( dhcontext ) );
            SessionCreated.Receive( dhcontext, BlockReceive( 304 ) );
#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "2 +" + TransportInstance.ToString() + "+" );
#endif

            SendRaw( SessionConfirmA.Send( dhcontext ) );
            SessionConfirmB.Receive( dhcontext, NTCPContext.RemoteRouterIdentity );

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( "3 +" + TransportInstance.ToString() + "+" );
#endif

            NTCPContext.SessionKey = dhcontext.SessionKey;
            NTCPContext.Encryptor = dhcontext.Encryptor;
            NTCPContext.Dectryptor = dhcontext.Dectryptor;
        }

    }
}
