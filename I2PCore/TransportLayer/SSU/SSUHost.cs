using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using System.Threading;
using I2PCore.SessionLayer;
using I2PCore.Data;
using System.Net.Sockets;

namespace I2PCore.TransportLayer.SSU
{
    [TransportProtocol]
    public partial class SSUHost: ITransportProtocol
    {
        Thread Worker;
        public bool Terminated { get; protected set; }

        public event Action<ITransport,I2PIdentHash> ConnectionCreated;

        public static readonly bool PeerTestSupported = true;
        public static readonly bool IntroductionSupported = true;

        internal delegate void RelayResponseInfo( SSUHeader header, RelayResponse response, IPEndPoint ep );

        readonly object RelayResponseReceivedLock = new object();
        internal event RelayResponseInfo RelayResponseReceived;

        long IncommingConnectionAttempts;

        public readonly EndpointStatistics EPStatisitcs = new EndpointStatistics();

#if DEBUG
        const int SessionCallWarningLevelMilliseconds = 400;

        public SuccessRatio MACCheck = new SuccessRatio();
        public SuccessRatio MACCheckFailIsIPV4 = new SuccessRatio();
#endif

        RouterContext MyRouterContext;
        HashSet<IPAddress> OurIPs;

        public SSUHost()
        {
            MyRouterContext = RouterContext.Inst;
            MyRouterContext.NetworkSettingsChanged += new Action( NetworkSettingsChanged );

            OurIPs = new HashSet<IPAddress>( Dns.GetHostEntry( Dns.GetHostName() ).AddressList );

            UpdateRouterContext();

            Worker = new Thread( Run )
            {
                Name = "SSUHost",
                IsBackground = true
            };
            Worker.Start();
        }

        internal bool AllowConnectToSelf { get; set; } = false;

        LinkedList<IPAddress> ReportedAddresses = new LinkedList<IPAddress>();
        TickCounter LastIPReport = null;
        TickCounter LastExternalIPProcess = TickCounter.MaxDelta;

        internal void ReportedAddress( IPAddress ipaddr )
        {
            if ( LastExternalIPProcess.DeltaToNowSeconds < ( LastIPReport == null ? 1 : 60 ) ) return;
            if ( ipaddr.AddressFamily != AddressFamily.InterNetwork ) return;
            LastExternalIPProcess.SetNow();

            Logging.LogTransport( $"SSU My IP: My external IP {ipaddr}" );

            lock ( ReportedAddresses )
            {
                ReportedAddresses.AddLast( ipaddr );
                while ( ReportedAddresses.Count > 200 ) ReportedAddresses.RemoveFirst();

                var first = ReportedAddresses.First.Value;
                var firstbytes = first.GetAddressBytes();
                if ( ReportedAddresses.Count() > 10 && ReportedAddresses.All( a => BufUtils.Equal( a.GetAddressBytes(), firstbytes ) ) )
                {
                    Logging.LogTransport( $"SSU My IP: Start using unanimous remote reported external IP {ipaddr}" );
                    UpdateSSUReportedAddr( ipaddr );
                }
                else
                {
                    var freq = ReportedAddresses.GroupBy( a => a.GetAddressBytes() ).OrderBy( g => g.Count() );
                    if ( freq.First().Count() > 15 )
                    {
                        Logging.LogTransport( $"SSU My IP: Start using most frequently reported remote external IP {ipaddr}" );
                        UpdateSSUReportedAddr( ipaddr );
                    }
                }
            }
        }

        internal void ReportConnectionCreated( SSUSession sess, I2PIdentHash routerid )
        {
#if DEBUG
            if ( ConnectionCreated == null )
                    Logging.LogWarning( $"SSUHost: No observers for ConnectionCreated!" );
#endif
            ConnectionCreated?.Invoke( sess, routerid );
        }

        private void UpdateSSUReportedAddr( IPAddress ipaddr )
        {
            if ( LastIPReport?.DeltaToNow.ToMinutes < 30 ) return;
            if ( LastIPReport == null ) LastIPReport = new TickCounter();
            LastIPReport.SetNow();

            MyRouterContext.SSUReportedAddr( ipaddr );
            UpdateRouterContext();
        }

        internal void ReportRelayResponse( SSUHeader header, RelayResponse response, IPEndPoint ep )
        {
            if ( RelayResponseReceived != null )
            {
                lock ( RelayResponseReceivedLock )
                {
                    RelayResponseReceived( header, response, ep );
                }
            }
        }

        public ProtocolCapabilities ContactCapability( I2PRouterInfo router )
        {
            return router.Adresses.Any( ra => ra.TransportStyle == "SSU"
                        && ra.Options.Contains( "key" ) )
                            ? ProtocolCapabilities.NATTraversal
                            : ProtocolCapabilities.None;
        }
    }
}
