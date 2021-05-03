using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public partial class RouterContext
    {
        public bool UPnpExternalAddressAvailable = false;
        public IPAddress UPnpExternalAddress;
        public bool UPnpExternalTCPPortMapped = false;
        public int UPnpExternalTCPPort;
        public bool UPnpExternalUDPPortMapped = false;
        public int UPnpExternalUDPPort;
        public IPAddress SSUReportedExternalAddress;

        public static IEnumerable<UnicastIPAddressInformation> GetAllLocalInterfaces(
            IEnumerable<NetworkInterfaceType> types,
            IEnumerable<AddressFamily> families )
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                           .Where( x => types.Any( t => t == x.NetworkInterfaceType )
                                && x.OperationalStatus == OperationalStatus.Up )
                           .SelectMany( x => x.GetIPProperties().UnicastAddresses )
                           .Where( x => families.Any( f => f == x.Address.AddressFamily ) )
                           .ToArray();
        }

        NetworkInterfaceType[] InterfaceTypes = new NetworkInterfaceType[]
        {
            NetworkInterfaceType.Ethernet,
            NetworkInterfaceType.Wireless80211
        };

        public IPAddress ExtIPV4Address
        {
            get
            {
                if ( UPnpExternalAddressAvailable )
                {
                    return UPnpExternalAddress;
                }

                if ( SSUReportedExternalAddress != null )
                {
                    return SSUReportedExternalAddress;
                }

                if ( DefaultExtAddress != null ) return DefaultExtAddress;

                return GetAllLocalInterfaces(
                        InterfaceTypes,
                        new AddressFamily[]
                        {
                            AddressFamily.InterNetwork
                        } )
                    ?.Random()?.Address;
            }
        }
        public void SSUReportedAddr( IPAddress extaddr )
        {
            if ( extaddr == null ) return;
            if ( SSUReportedExternalAddress != null && SSUReportedExternalAddress.Equals( extaddr ) ) return;

            SSUReportedExternalAddress = extaddr;
            ClearCache();
        }

        internal void UpnpReportedAddr( string addr )
        {
            if ( UPnpExternalAddressAvailable && UPnpExternalAddress.Equals( IPAddress.Parse( addr ) ) ) return;

            UPnpExternalAddress = IPAddress.Parse( addr );
            UPnpExternalAddressAvailable = true;
            ClearCache();
        }

        internal void UpnpNATPortMapAdded( IPAddress addr, string protocol, int port )
        {
            if ( protocol == "TCP" && UPnpExternalTCPPortMapped && UPnpExternalTCPPort == port ) return;
            if ( protocol == "UDP" && UPnpExternalUDPPortMapped && UPnpExternalUDPPort == port ) return;

            if ( protocol == "TCP" )
            {
                UPnpExternalTCPPortMapped = true;
                UPnpExternalTCPPort = port;
            }
            else
            {
                UPnpExternalUDPPortMapped = true;
                UPnpExternalUDPPort = port;
            }
            UPnpExternalAddressAvailable = true;
            ClearCache();

            ApplyNewSettings();
        }
    }
}
