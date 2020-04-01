using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.Net;
using System.IO;
using I2PCore.Utils;
using I2PCore.TransportLayer.SSU;
using System.Net.Sockets;
using I2PCore.TransportLayer.SSU.Data;
using System.Net.NetworkInformation;

// Todo list for all of I2PCore
// TODO: SSU PeerTest with automatic firewall detection
// TODO: Add IPV6
// TODO: NTCP does not close the old listen socket when settings change.
// TODO: Replace FailedToConnectException with return value?
// TODO: IP block lists for incomming connections, NTCP
// TODO: Add transport bandwidth statistics
// TODO: Implement bandwidth limits (tunnels)
// TODO: Add the cert / key split support for ECDSA_SHA512_P521
// TODO: Add DatabaseLookup query support
// TODO: Add floodfill server support
// TODO: Implement connection limits (external)
// TODO: Refactor NTCP using async and await, and remove Watchdog
// TODO: Add decaying Bloom filters and remove packet duplicates

namespace I2PCore.SessionLayer
{
    public class RouterContext
    {
        private bool IsFirewalledField = true;

        public bool IsFirewalled
        {
            get => IsFirewalledField;
            set
            {
                IsFirewalledField = value;
                ClearCache();
            }
        }

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

        // IP settings
        public IPAddress DefaultExtAddress = null;
        public IPAddress ExtAddress
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

                return Address;
            }
        }

        public IPAddress Address 
        {
            get
            {
                IEnumerable<UnicastIPAddressInformation> ai;

                if ( false ) // TODO: IPV6
                {
                    ai = GetAllLocalInterfaces(
                        InterfaceTypes,
                        new AddressFamily[]
                        {
                            AddressFamily.InterNetwork,
                            AddressFamily.InterNetworkV6
                        } );
                }
                else
                {
                    ai = GetAllLocalInterfaces(
                        InterfaceTypes,
                        new AddressFamily[]
                        {
                            AddressFamily.InterNetwork
                        } );
                }

                return ai.Random().Address;
            }
        }

        public int DefaultTCPPort = 12123;
        public int TCPPort
        {
            get
            {
                if ( UPnpExternalTCPPortMapped )
                {
                    return UPnpExternalTCPPort;
                }
                return DefaultTCPPort;
            }
        }

        public int DefaultUDPPort = 12123;
        public int UDPPort
        {
            get
            {
                if ( UPnpExternalUDPPortMapped )
                {
                    return UPnpExternalUDPPort;
                }
                return DefaultUDPPort;
            }
        }

        public bool UPnpExternalAddressAvailable = false;
        public IPAddress UPnpExternalAddress;
        public bool UPnpExternalTCPPortMapped = false;
        public int UPnpExternalTCPPort;
        public bool UPnpExternalUDPPortMapped = false;
        public int UPnpExternalUDPPort;

        public bool UseIpV6 = false;

        public IPAddress SSUReportedExternalAddress;

        public event Action NetworkSettingsChanged;

        // I2P
        public I2PDate Published { get; private set; }
        public I2PCertificate Certificate { get; private set; }
        public I2PPrivateKey PrivateKey { get; private set; }
        public I2PPublicKey PublicKey { get; private set; }

        public I2PSigningPrivateKey PrivateSigningKey { get; private set; }
        public I2PSigningPublicKey PublicSigningKey { get; private set; }

        public I2PRouterIdentity MyRouterIdentity { get; private set; }

        public bool FloodfillEnabled = false;

        // SSU
        public BufLen IntroKey = new BufLen( new byte[32] );

        // Store

        public static string RouterPath
        {
            get
            {
                return Path.GetFullPath( StreamUtils.AppPath );
            }
        }

        public static string GetFullPath( string filename )
        {
            return Path.Combine( RouterPath, filename );
        }

        public static string RouterSettingsFile = "Router.bin";

        static RouterContext StaticInstance;
        public static RouterContext Inst
        {
            get
            {
                if ( StaticInstance != null ) return StaticInstance;
                StaticInstance = new RouterContext( RouterSettingsFile );
                return StaticInstance;
            }
            set
            {
                if ( StaticInstance != null )
                {
                    throw new InvalidOperationException( "Router context already establshed" );
                }

                StaticInstance = value;
            }
        }

        public RouterContext(): this( (I2PCertificate)null )
        {
        }

        public RouterContext( I2PCertificate cert )
        {
            NewIdentity( cert );
        }

        public RouterContext( string filename )
        {
            try
            {
                Logging.LogInformation( "RouterContext: Path: " + RouterPath );
                Load( GetFullPath( filename ) );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
                NewIdentity( null );
                Save( RouterSettingsFile );
            }
        }

        private void NewIdentity( I2PCertificate cert )
        {
            Published = new I2PDate( DateTime.UtcNow.AddMinutes( -1 ) );
            Certificate = cert ?? new I2PCertificate( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            //Certificate = new I2PCertificate( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            //Certificate = new I2PCertificate( I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256 );
            //Certificate = new I2PCertificate( I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384 );
            //Certificate = new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );
            PrivateSigningKey = new I2PSigningPrivateKey( Certificate );
            PublicSigningKey = new I2PSigningPublicKey( PrivateSigningKey );

            var keys = I2PPrivateKey.GetNewKeyPair();
            PrivateKey = keys.PrivateKey;
            PublicKey = keys.PublicKey;

            MyRouterIdentity = new I2PRouterIdentity( PublicKey, PublicSigningKey );
            IntroKey.Randomize();
        }

        private void Load( string filename )
        {
            using ( var fs = new FileStream( filename, FileMode.Open, FileAccess.Read ) )
            {
                using ( var ms = new MemoryStream() )
                {
                    byte[] buf = new byte[8192];
                    int len;
                    while ( ( len = fs.Read( buf, 0, buf.Length ) ) != 0 ) ms.Write( buf, 0, len );

                    var reader = new BufRefLen( ms.ToArray() );

                    Certificate = new I2PCertificate( reader );
                    PrivateSigningKey = new I2PSigningPrivateKey( reader, Certificate );
                    PublicSigningKey = new I2PSigningPublicKey( reader, Certificate );

                    PrivateKey = new I2PPrivateKey( reader, Certificate );
                    PublicKey = new I2PPublicKey( reader, Certificate );

                    MyRouterIdentity = new I2PRouterIdentity( reader );
                    Published = new I2PDate( reader );
                    IntroKey = reader.ReadBufLen( 32 );
                }
            }
        }

        public void Save( string filename )
        {
            var fullpath = GetFullPath( filename );

            using ( var fs = new FileStream( fullpath, FileMode.Create, FileAccess.Write ) )
            {
                var dest = new BufRefStream();

                Certificate.Write( dest );
                PrivateSigningKey.Write( dest );
                PublicSigningKey.Write( dest );

                PrivateKey.Write( dest );
                PublicKey.Write( dest );

                MyRouterIdentity.Write( dest );
                Published.Write( dest );
                IntroKey.WriteTo( dest );

                var ar = dest.ToArray();
                fs.Write( ar, 0, ar.Length );
            }
        }

        TickCounter MyRouterInfoCacheCreated = TickCounter.MaxDelta;
        I2PRouterInfo MyRouterInfoCache = null;

        public I2PRouterInfo MyRouterInfo
        {
            get
            {
                lock ( MyRouterInfoCacheCreated )
                {
                    var cache = MyRouterInfoCache;
                    if ( cache != null &&
                        MyRouterInfoCacheCreated.DeltaToNow < NetDb.RouterInfoExpiryTime / 3 )
                    {
                        return cache;
                    }

                    MyRouterInfoCacheCreated.SetNow();

                    var caps = new I2PMapping();

                    var capsstring = "LPR";
                    if ( FloodfillEnabled ) capsstring += "f";

                    caps["caps"] = capsstring;

                    caps["netId"] = I2PConstants.I2P_NETWORK_ID.ToString();
                    caps["coreVersion"] = I2PConstants.PROTOCOL_VERSION;
                    caps["router.version"] = I2PConstants.PROTOCOL_VERSION;
                    caps["stat_uptime"] = "90m";

                    var ntcp = new I2PRouterAddress( ExtAddress, TCPPort, 11, "NTCP" );
                    var ssu = new I2PRouterAddress( ExtAddress, UDPPort, 5, "SSU" );

                    var ssucaps = "";
                    if ( SSUHost.PeerTestSupported ) ssucaps += "B";
                    if ( SSUHost.IntroductionSupported ) ssucaps += "C";

                    ssu.Options["caps"] = ssucaps;
                    ssu.Options["key"] = FreenetBase64.Encode( IntroKey );
                    foreach ( var intro in SSUIntroducersInfo )
                    {
                        ssu.Options[intro.Key] = intro.Value;
                    }

                    var result = new I2PRouterInfo(
                        MyRouterIdentity,
                        new I2PDate( DateTime.UtcNow.AddMinutes( -1 ) ),
                        IsFirewalled
                            ? new I2PRouterAddress[] { ssu }
                            : new I2PRouterAddress[] { ntcp, ssu },
                        caps,
                        PrivateSigningKey );

                    MyRouterInfoCache = result;
                    NetDb.Inst.FloodfillUpdate.TrigUpdateRouterInfo( "MyRouterInfo changed" );

                    Logging.Log( $"RouterContext: New settings: {result}" );

                    return result;
                }
            }
        }

        private void ClearCache()
        {
            MyRouterInfoCache = null;
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

        public void ApplyNewSettings()
        {
            ClearCache();
            if ( NetworkSettingsChanged != null ) NetworkSettingsChanged();
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

        List<KeyValuePair<string,string>> SSUIntroducersInfo = new List<KeyValuePair<string, string>>();

        internal void NoIntroducers()
        {
            if ( SSUIntroducersInfo.Any() )
            {
                SSUIntroducersInfo = new List<KeyValuePair<string, string>>();
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

            foreach( var one in introducers )
            {
                result.Add( new KeyValuePair<string, string>( $"ihost{ix}", one.Host.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"iport{ix}", one.Port.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"ikey{ix}", FreenetBase64.Encode( one.IntroKey ) ) );
                result.Add( new KeyValuePair<string, string>( $"itag{ix}", one.IntroTag.ToString() ) );
                ++ix;
            }

            SSUIntroducersInfo = result;
            ClearCache();
        }
    }
}
