#define NO_MANUAL_SIGN

using System;
using I2PCore.Data;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using System.Net;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore;

namespace I2PDemo
{
    class Program
    {
        static I2PDestinationInfo MyDestinationInfo;
        static I2PDestination MyDestination;

        static ClientDestination PublishedDestination;

        static I2PDestinationInfo MyOriginInfo;
        static ClientDestination MyOrigin;

        static void Main( string[] args )
        {
            PeriodicAction SendInterval = new PeriodicAction( TickSpan.Seconds( 20 ) );

            Logging.ReadAppConfig();
            Logging.LogToDebug = false;
            Logging.LogToConsole = true;

            MyDestinationInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );

            for ( int i = 0; i < args.Length; ++i )
            {
                switch ( args[i] )
                {
                    case "--addr":
                    case "--address":
                        if ( args.Length > i + 1 ) 
                        {
                            RouterContext.Inst.DefaultExtAddress = IPAddress.Parse( args[++i] );
                            Console.WriteLine( $"addr {RouterContext.Inst.DefaultExtAddress}" );
                        }
                        else
                        {
                            Console.WriteLine( "--addr require ip number" );
                            return;
                        }
                        break;

                    case "--port":
                        if ( args.Length > i + 1 )
                        {
                            var port = int.Parse( args[++i] );
                            RouterContext.Inst.DefaultTCPPort = port;
                            RouterContext.Inst.DefaultUDPPort = port;
                            Console.WriteLine( $"port {port}" );
                        }
                        else
                        {
                            Console.WriteLine( "--port require port number" );
                            return;
                        }
                        break;
                        
                    case "--nofw":
                        RouterContext.Inst.IsFirewalled = false;
                        Console.WriteLine( $"Firewalled {RouterContext.Inst.IsFirewalled}" );
                        break;

                    case "--mkdest":
                    case "--create-destination":
                        var certtype = 0;
                        if ( args.Length > i + 1 )
                        {
                            certtype = int.Parse( args[++i] );
                        }

                        I2PSigningKey.SigningKeyTypes ct;
                        I2PDestinationInfo d;

                        switch ( certtype )
                        {
                            default:
                            case 0:
                                ct = I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519;
                                d = new I2PDestinationInfo( ct );
                                break;

                            case 1:
                                ct = I2PSigningKey.SigningKeyTypes.DSA_SHA1;
                                d = new I2PDestinationInfo( ct );
                                break;

                            case 2:
                                ct = I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256;
                                d = new I2PDestinationInfo( ct );
                                break;

                            case 3:
                                ct = I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384;
                                d = new I2PDestinationInfo( ct );
                                break;
                        }

                        Console.WriteLine( $"New destination {ct}: {d.ToBase64()}" );
                        return;

                    case "--destination":
                        if ( args.Length > i + 1 )
                        {
                            MyDestinationInfo = new I2PDestinationInfo( args[++i] );
                            Console.WriteLine( $"Destination {MyDestinationInfo}" );
                        }
                        else
                        {
                            Console.WriteLine( "Base64 encoded Destination required" );
                            return;
                        }
                        break;

                    default:
                        Console.WriteLine( args[i] );
                        Console.WriteLine( "Usage: I2P.exe --addr 12.34.56.78 --port 8081 --nofw --create-destination [0-3] --destination b64..." );
                        break;
                }
            }

            RouterContext.Inst.ApplyNewSettings();

            var pnp = new UPnp();
            Thread.Sleep( 5000 ); // Give UPnp a chance

            Router.Start();

            // Create new identities for this run

            MyDestination = MyDestinationInfo.Destination;

#if MANUAL_SIGN
            PublishedDestination = Router.CreateDestination( 
                    MyDestination, 
                    MyDestinationInfo.PrivateKey, 
                    true ); // Publish our destinaiton
            PublishedDestination.SignLeasesRequest += MyDestination_SignLeasesRequest;
#else
            PublishedDestination = Router.CreateDestination( MyDestinationInfo, true, out _ ); // Publish our destinaiton
#endif
            PublishedDestination.DataReceived += MyDestination_DataReceived;
            PublishedDestination.Name = "PublishedDestination";

            MyOriginInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );
            MyOrigin = Router.CreateDestination( MyOriginInfo, false, out _ );
            MyOrigin.ClientStateChanged += MyOrigin_ClientStateChanged;
            MyOrigin.DataReceived += MyOrigin_DataReceived;
            MyOrigin.Name = "MyOrigin";

            Logging.LogInformation( $"MyDestination: {PublishedDestination.Destination.IdentHash} {MyDestinationInfo.Destination.Certificate}" );

            while ( true )
            {
                try
                {
                    Connected = true;

                    MyOrigin.LookupDestination( PublishedDestination.Destination.IdentHash, LookupResult );

                    var sendevents = 0;

                    while ( Connected )
                    {
                        Thread.Sleep( 2000 );

                        if ( LookedUpDestination != null 
                            && MyOrigin.ClientState == ClientDestination.ClientStates.Established )
                        {
                            SendInterval.Do( () =>
                            {
                                if ( sendevents++ < 10 )
                                {
                                    // Send some data to the MyDestination
                                    DataSent = new BufLen(
                                                    BufUtils.RandomBytes(
                                                        (int)( 1 + BufUtils.RandomDouble( 25 ) * 1024 ) ) );

                                    var ok = MyOrigin.Send( LookedUpDestination, DataSent );
                                    Logging.LogInformation( $"Program {MyOrigin}: Send[{sendevents}] {ok}, {DataSent:15}" );
                                }

                                if ( sendevents > 100 ) sendevents = 0;
                            } );
                        }
                    }
                }
                catch ( SocketException ex )
                {
                    Logging.Log( ex );
                }
                catch ( IOException ex )
                {
                    Logging.Log( ex );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        static void MyOrigin_ClientStateChanged( ClientDestination dest, ClientDestination.ClientStates state )
        {
            Logging.LogInformation( $"Program {MyOrigin}: Client state {state}" );
        }

        static I2PDestination LookedUpDestination;
        static BufLen DataSent;

        static void MyDestination_DataReceived( ClientDestination dest, BufLen data )
        {
            var compareok = DataSent is null ? false : DataSent == data;
            Logging.LogInformation( $"Program {MyDestination}: MyDestination data received. Matches send: {compareok} {data:15}" );
            PublishedDestination.Send( MyOrigin.Destination, data );
        }

        static void MyOrigin_DataReceived( ClientDestination dest, BufLen data )
        {
            Logging.LogInformation( $"Program {MyOrigin}: data received. {data:15}" );
        }

        static void MyDestination_SignLeasesRequest( I2PLeaseSet ls )
        {
            Logging.LogInformation( $"Program {MyDestination}: Signing {ls} for publishing" );
            PublishedDestination.SignedLeases = new I2PLeaseSet(
                    MyDestination,
                    ls.Leases,
                    new I2PLeaseInfo( MyDestinationInfo ) );
        }

        static void LookupResult( I2PIdentHash hash, I2PLeaseSet ls, object o )
        {
            Logging.LogInformation( $"Program {MyOrigin}: LookupResult {hash.Id32Short} {ls}" );

            if ( ls is null )
            {
                // Try again
                MyOrigin.LookupDestination( PublishedDestination.Destination.IdentHash, LookupResult );
                return;
            }

            LookedUpDestination = ls.Destination;
        }

        static bool Connected = false;
    }
}
