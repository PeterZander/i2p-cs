using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using System.Threading;
using I2PCore.SessionLayer;
using I2PCore.Data;
using I2PCore.TransportLayer.SSU.Data;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace I2PCore.TransportLayer.SSU
{
	public partial class SSUHost
	{
        List<KeyValuePair<string, string>> IntroducersInfo = new List<KeyValuePair<string, string>>();

        internal void NoIntroducers()
        {
            if ( IntroducersInfo.Any() )
            {
                IntroducersInfo = new List<KeyValuePair<string, string>>();
                UpdateRouterContext();
            }
        }

        internal void SetIntroducers( IEnumerable<IntroducerInfo> introducers )
        {
            if ( !introducers.Any() )
            {
                NoIntroducers();
                return;
            }

            var result = new List<KeyValuePair<string, string>>();
            var ix = 0;

            foreach ( var one in introducers )
            {
                result.Add( new KeyValuePair<string, string>( $"ihost{ix}", one.Host.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"iport{ix}", one.Port.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"ikey{ix}", FreenetBase64.Encode( one.IntroKey ) ) );
                result.Add( new KeyValuePair<string, string>( $"itag{ix}", one.IntroTag.ToString() ) );
                ++ix;
            }

            IntroducersInfo = result;

            UpdateRouterContext();
        }

        private void UpdateRouterContext()
        {
            var addrs = new List<I2PRouterAddress>();

            var addr = new I2PRouterAddress( RouterContext.Inst.ExtIPV4Address, RouterContext.Inst.UDPPort, 5, "SSU" );

            var ssucaps = "";
            if ( PeerTestSupported ) ssucaps += "B";
            if ( IntroductionSupported ) ssucaps += "C";

            addr.Options["caps"] = ssucaps;
            addr.Options["key"] = FreenetBase64.Encode( RouterContext.Inst.IntroKey );
            addr.Options["mtu"] = RouterContext.IPV4MTU.ToString();
            foreach ( var intro in IntroducersInfo )
            {
                addr.Options[intro.Key] = intro.Value;
            }
            addrs.Add( addr );

            if ( RouterContext.UseIpV6 )
            {
                var lv6 = GetLocalIpV6Address();
                var addr6 = new I2PRouterAddress( lv6, RouterContext.Inst.UDPPort, 5, "SSU" );

                addr6.Options["caps"] = ssucaps;
                addr6.Options["key"] = FreenetBase64.Encode( RouterContext.Inst.IntroKey );
                addr6.Options["mtu"] = RouterContext.IPV6MTU.ToString();
                addrs.Add( addr6 );
            }

            RouterContext.Inst.UpdateAddress( this, addrs );
        }
        static readonly IPAddressMask GlobalUnicast = new IPAddressMask( "2000::/3" );
        public static IPAddress GetLocalIpV6Address()
        {
            UnicastIPAddressInformation ipv6addr = null;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach( var interf in interfaces )
            {
                if ( interf.OperationalStatus != OperationalStatus.Up )
                    continue;

                var properties = interf.GetIPProperties();

                if ( properties.GatewayAddresses.Count == 0 )
                    continue;

                foreach( var addr in properties.UnicastAddresses )
                {
                    if ( IPAddress.IsLoopback( addr.Address )
                        || addr.Address.AddressFamily != AddressFamily.InterNetworkV6 )
                            continue;

                    if ( GlobalUnicast.BelongsTo( addr.Address ) )
                    {
                        ipv6addr = addr;
                        break;
                    }
                }
            }

            return ipv6addr?.Address;
        }
    }
}
