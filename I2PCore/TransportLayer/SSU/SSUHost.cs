using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using System.Threading;
using I2PCore.SessionLayer;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SSUHost
    {
        Thread Worker;
        public bool Terminated { get; protected set; }

        public event Action<ITransport> ConnectionCreated;

        public static readonly bool PeerTestSupported = true;
        public static readonly bool IntroductionSupported = true;

        internal delegate void RelayResponseInfo( SSUHeader header, RelayResponse response, IPEndPoint ep );

        readonly object RelayResponseReceivedLock = new object();
        internal event RelayResponseInfo RelayResponseReceived;

        long IncommingConnectionAttempts;

        public readonly EndpointStatistics EPStatisitcs = new EndpointStatistics();

#if DEBUG
        const int SessionCallWarningLevelMilliseconds = 450;
#endif

        RouterContext MyRouterContext;
        readonly IMTUProvider MTUProvider;
        HashSet<IPAddress> OurIPs;

        public SSUHost( RouterContext rc, IMTUProvider mtup )
        {
            MyRouterContext = rc;
            MTUProvider = mtup;
            MyRouterContext.NetworkSettingsChanged += new Action( NetworkSettingsChanged );

            OurIPs = new HashSet<IPAddress>( Dns.GetHostEntry( Dns.GetHostName() ).AddressList );

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

        private void UpdateSSUReportedAddr( IPAddress ipaddr )
        {
            if ( LastIPReport?.DeltaToNow.ToMinutes < 30 ) return;
            if ( LastIPReport == null ) LastIPReport = new TickCounter();
            LastIPReport.SetNow();

            MyRouterContext.SSUReportedAddr( ipaddr );
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
    }
}
