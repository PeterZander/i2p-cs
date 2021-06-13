#define NOMANUAL_SIGN

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
using System.Linq;
using System.Collections.Generic;

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
            PeriodicAction LookupInterval = new PeriodicAction( TickSpan.Minutes( 4 ) );

            Logging.LogToDebug = false;
            Logging.LogToConsole = true;
            Logging.ReadAppConfig();

            RouterContext.RouterSettingsFile = "I2PDemo.bin";

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

                    case "--ipv6":
                        RouterContext.UseIpV6 = true;
                        Console.WriteLine( $"Using IPV6" );
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

            Router.Start();

            // Create new identities for this run

            MyDestination = MyDestinationInfo.Destination;

#if MANUAL_SIGN
            PublishedDestination = Router.CreateDestination(
                    MyDestination, 
                    true,
                    out _ ); // Publish our destinaiton
            PublishedDestination.SignLeasesRequest += MyDestination_SignLeasesRequest;
            
            PublishedDestination.GenerateTemporaryKeys();
#else
            // Publish our destinaiton
            PublishedDestination = Router.CreateDestination(
                MyDestinationInfo,
                true, out _ );
#endif
            PublishedDestination.ClientStateChanged += MyDestination_ClientStateChanged;
            PublishedDestination.DataReceived += MyDestination_DataReceived;
            PublishedDestination.Name = "PublishedDestination";

            var ls = new I2PLeaseSet( MyDestinationInfo.Destination, null, 
                    MyDestinationInfo.Destination.PublicKey,
                    MyDestinationInfo.Destination.SigningPublicKey,
                    MyDestinationInfo.PrivateSigningKey );

            // Caller
            
            MyOriginInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );

            MyOrigin = Router.CreateDestination(
                    MyOriginInfo,
                    false,
                    out _ );

            MyOrigin.ClientStateChanged += MyOrigin_ClientStateChanged;
            MyOrigin.DataReceived += MyOrigin_DataReceived;
            MyOrigin.Name = "MyOrigin";

            Logging.LogInformation( $"MyDestination: {PublishedDestination.Destination.IdentHash} {MyDestinationInfo.Destination.Certificate}" );

            while ( true )
            {
                try
                {
                    Connected = true;

                    var sendevents = 0;

                    while ( Connected )
                    {
                        Thread.Sleep( 2000 );

                        if ( MyOrigin.ClientState == ClientDestination.ClientStates.Established )
                        {
                            if ( LookedUpDestination == null )
                            {
                                MyOrigin.LookupDestination( PublishedDestination.Destination.IdentHash, LookupResult );
                            }
                            else
                            {
                                LookupInterval.Do( () =>
                                {
                                    // Keep remote leases up to date
                                    MyOrigin.LookupDestination( PublishedDestination.Destination.IdentHash, LookupResult );
                                } );

                                SendInterval.Do( () =>
                                {
                                    if ( sendevents++ < 20 )
                                    {
                                        // Send some data to the MyDestination
                                        DataSent = new BufLen(
                                                        BufUtils.RandomBytes(
                                                            (int)( 1 + BufUtils.RandomDouble( 25 ) * 1024 ) ) );

                                        var ok = MyOrigin.Send( LookedUpDestination, DataSent );
                                        Logging.LogInformation( $"Program {MyOrigin}: Send[{sendevents}] to {LookedUpDestination.IdentHash.Id32Short} {ok}, {DataSent:15}" );
                                    }

                                    if ( sendevents > 70 ) sendevents = 0;
                                } );
                            }
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

        static void MyDestination_ClientStateChanged( ClientDestination dest, ClientDestination.ClientStates state )
        {
            Logging.LogInformation( $"Program {MyDestination}: Client state {state}" );
        }
        
        static void MyDestination_DataReceived( ClientDestination dest, BufLen data )
        {
            var compareok = DataSent is null ? false : DataSent == data;
            Logging.LogInformation( $"Program {MyDestination}: MyDestination data received. Matches send: {compareok} {data:15}" );
            var ok = PublishedDestination.Send( MyOrigin.Destination, data );
            Logging.LogInformation( $"Program {MyDestination}: Send to {MyOrigin.Destination.IdentHash.Id32Short} {ok}" );
        }

        static void MyOrigin_DataReceived( ClientDestination dest, BufLen data )
        {
            Logging.LogInformation( $"Program {MyOrigin}: data received. {data:15}" );
        }

        static void MyDestination_SignLeasesRequest( ClientDestination dest, IEnumerable<ILease> ls )
        {
            Logging.LogInformation( $"Program {MyDestination}: Signing {ls} for publishing" );
            PublishedDestination.PrivateKeys = new List<I2PPrivateKey>( new I2PPrivateKey[] { MyDestinationInfo.PrivateKey } );
            PublishedDestination.SignedLeases = new I2PLeaseSet(
                    MyDestination,
                    ls.Select( l => new I2PLease( l.TunnelGw, l.TunnelId, new I2PDate( l.Expire ) ) ),
                    MyDestination.PublicKey,
                    MyDestination.SigningPublicKey,
                    MyDestinationInfo.PrivateSigningKey );
        }

        static void LookupResult( I2PIdentHash hash, ILeaseSet ls, object o )
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
