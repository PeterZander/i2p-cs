using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.Net;
using System.IO;
using I2PCore.Utils;
using System.Collections.Concurrent;
using I2PCore.TransportLayer;
using System.Net.Sockets;

// Todo list for all of I2PCore
// TODO: SSU PeerTest with automatic firewall detection
// TODO: NTCP does not close the old listen socket when settings change.
// TODO: Replace FailedToConnectException with return value?
// TODO: IP block lists for incomming connections, NTCP
// TODO: Implement bandwidth limits (tunnels)
// TODO: Add the cert / key split support for ECDSA_SHA512_P521
// TODO: Add DatabaseLookup query support
// TODO: Add floodfill server support
// TODO: Implement connection limits (external)
// TODO: Refactor NTCP using async and await, and remove Watchdog
// TODO: Add decaying Bloom filters and remove packet duplicates

namespace I2PCore.SessionLayer
{
    public partial class RouterContext
    {
        public const int IPV4_HEADER_SIZE = 20;
        public const int IPV6_HEADER_SIZE = 40;
        public const int UDP_HEADER_SIZE = 8;
        public const int IPV6MTU = 1488;
        public const int IPV4MTU = 1484;

        public static int MaxPacketSize( AddressFamily af, int mtu )
        {
            if ( mtu <= 0 )
            {
                throw new ArgumentException( "MTU must be > 0" );
            }

            var result = af == AddressFamily.InterNetwork
                    ? mtu - IPV4_HEADER_SIZE - UDP_HEADER_SIZE
                    : mtu - IPV6_HEADER_SIZE - UDP_HEADER_SIZE;

            return result & ( ~0xf );
        }

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

        public IPAddress LocalInterface { get => UseIpV6 ? IPAddress.IPv6Any : IPAddress.Any; }

        // IP settings
        public IPAddress DefaultExtAddress = null;

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

        private static bool UseIpV4Field = true;
        public static bool UseIpV4
        {
            get => UseIpV4Field;
            set
            {
                if ( TransportProvider.Inst != null )
                {
                    throw new Exception( "Transport provider have already been started" );
                }
                
                UseIpV4Field = value;
            }
        }

        private static bool UseIpV6Field = false;
        public static bool UseIpV6
        {
            get => UseIpV6Field;
            set
            {
                if ( TransportProvider.Inst != null )
                {
                    throw new Exception( "Transport provider have already been started" );
                }
                
                UseIpV6Field = value;
            }
        }

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

        /// <summary>
        /// The router settings file containing router id and intro keys.
        /// If you want to change this, do it before Router.Start() is called.
        /// </summary>
        public static string RouterSettingsFile = "Router.bin";

        static RouterContext StaticInstance;
        static readonly object StaticInstanceLock = new object();

        /// <summary>
        /// Singleton access to the instance of RouterContext.
        /// </summary>
        /// <value>The inst.</value>
        public static RouterContext Inst
        {
            get
            {
                lock ( StaticInstanceLock )
                {
                    if ( StaticInstance != null ) return StaticInstance;
                    StaticInstance = new RouterContext( RouterSettingsFile );
                    return StaticInstance;
                }
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
                Logging.LogInformation( $"RouterContext: Path: {RouterPath}" );
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

                    var addresses = RouterAdresses.Values.SelectMany( a => a ).ToArray();
                    var result = new I2PRouterInfo(
                        MyRouterIdentity,
                        new I2PDate( DateTime.UtcNow.AddMinutes( -1 ) ),
                        addresses,
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

        readonly ConcurrentDictionary<ITransportProtocol, List<I2PRouterAddress>> RouterAdresses
            = new ConcurrentDictionary<ITransportProtocol, List<I2PRouterAddress>>();

        public void UpdateAddress( ITransportProtocol proto, List<I2PRouterAddress> addrs )
        {
            if ( addrs is null )
            {
                RouterAdresses.TryRemove( proto, out _ );
                ClearCache();
                return;
            }

            RouterAdresses[proto] = addrs;
            ClearCache();
        }

        /// <summary>
        /// Force recreation of the RouterInfo for this instance.
        /// </summary>
        public void ApplyNewSettings()
        {
            ClearCache();
            NetworkSettingsChanged?.Invoke();
        }
    }
}
