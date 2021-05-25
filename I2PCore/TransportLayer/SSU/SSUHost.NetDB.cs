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
                var unwrappedaddr = one.Host.IsIPv4MappedToIPv6 ? one.Host.MapToIPv4() : one.Host;
                result.Add( new KeyValuePair<string, string>( $"ihost{ix}", unwrappedaddr.ToString() ) );
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

            var ssucaps = "";
            if ( PeerTestSupported ) ssucaps += "B";
            if ( IntroductionSupported ) ssucaps += "C";

            if ( RouterContext.UseIpV4 )
            {
                var addr4 = new I2PRouterAddress( RouterContext.Inst.ExtIPV4Address, RouterContext.Inst.UDPPort, 5, "SSU" );

                addr4.Options["caps"] = ssucaps;
                addr4.Options["key"] = FreenetBase64.Encode( RouterContext.Inst.IntroKey );
                addr4.Options["mtu"] = RouterContext.IPV4MTU.ToString();
                foreach ( var intro in IntroducersInfo )
                {
                    addr4.Options[intro.Key] = intro.Value;
                }
                addrs.Add( addr4 );
            }

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
            var addrs = GetLocalIpV6Addresses();

            try
            {
                return addrs 
                    .OrderByDescending( uiai => uiai.AddressValidLifetime )
                    .Select( uiai => uiai.Address )
                    .FirstOrDefault();
            }
            catch( PlatformNotSupportedException )
            {
                return addrs 
                    .Select( uiai => uiai.Address )
                    .FirstOrDefault();
            }
        }

        public static IEnumerable<UnicastIPAddressInformation> GetLocalIpV6Addresses()
        {
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
                        yield return addr;
                    }
                }
            }
        }
    }
}
