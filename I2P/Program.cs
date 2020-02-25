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

namespace I2P
{
    class Program
    {
        static I2PDestinationInfo MyDestinationInfo;
        static ClientDestination MyDestination;

        static I2PDestinationInfo MyOriginInfo;
        static ClientOrigin MyOrigin;

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

            MyDestination = Router.CreateDestination( MyDestinationInfo, true ); // Publish our destinaiton

            MyOriginInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );
            MyOrigin = Router.CreateOrigin( MyOriginInfo, MyDestination.Destination ); // Contact ourselfs
            MyOrigin.ClientStateChanged += MyOrigin_ClientStateChanged;

            MyDestination.DataReceived += MyDestination_DataReceived;

            Logging.LogInformation( $"MyDestination: {MyDestinationInfo.Certificate} {MyDestination.Destination.IdentHash}" );

            while ( true )
            {
                try
                {
                    Connected = true;

                    var hashes = new I2PIdentHash[] {
                        new I2PIdentHash( "udhdrtrcetjm5sxaskjyr5ztseszydbh4dpl3pl4utgqqw2v4jna.b32.i2p" ),
                        new I2PIdentHash( "7tbay5p4kzemkxvyvbf6v7eau3zemsnnl2aoyqhg5jzpr5eke7tq.b32.i2p" ),
                        new I2PIdentHash( "ukeu3k5oycga3uneqgtnvselmt4yemvoilkln7jpvafvfx7dnkdq.b32.i2p" )
                    };

                    while ( Connected )
                    {
                        Thread.Sleep( 2000 );

                        SendInterval.Do( () =>
                        {
                            //MyOrigin.LookupDestination( MyDestination.Destination.IdentHash, LookupResult );

                            // Send some data to the MyDestination
                            DataSent = new BufLen(
                                    BufUtils.Random(
                                        (int)( 1 + BufUtils.RandomDouble( 25 ) * 1024 ) ) );

                            var ok = MyOrigin.Send( DataSent );
                            Logging.LogInformation( $"Program: Send {ok}, {DataSent:15}" );
                        } );
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

        static void MyOrigin_ClientStateChanged( Client.ClientStates state )
        {
            Logging.LogInformation( $"Program: Client state {state}" );
        }

        static BufLen DataSent;

        static void MyDestination_DataReceived( BufLen data )
        {
            var compareok = DataSent is null ? false : DataSent == data;
            Logging.LogInformation( $"Program: MyDestination data received. Matches send: {compareok} {data:15}" );
        }

        static void LookupResult( I2PIdentHash hash, I2PLeaseSet ls )
        {
            Logging.LogInformation( $"Program: LookupResult {hash.Id32Short} {ls}" );
        }

        static bool Connected = false;
    }
}
