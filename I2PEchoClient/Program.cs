#define NO_MANUAL_SIGN

using System;
using I2PCore.Data;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using System.Net;
using I2P.Streaming;
using System.Collections.Generic;
using static I2P.Streaming.StreamingPacket;
using static I2P.I2CP.Messages.I2CPMessage;
using static System.Configuration.ConfigurationManager;

namespace I2PEchoClient
{
    class Program
    {
        static I2PDestinationInfo MyDestinationInfo;
        static ClientDestination UnpublishedDestination;

        static bool Connected = false;

        static void Main( string[] args )
        {
            Logging.ReadAppConfig();
            Logging.LogToDebug = false;
            Logging.LogToConsole = true;

            RouterContext.RouterSettingsFile = "EchoClientRouter.bin";
            //RouterContext.Inst = new RouterContext(
                        //new I2PCertificate(
                                //I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 ) );

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

                    default:
                        Console.WriteLine( args[i] );
                        Console.WriteLine( "Usage: I2P.exe --addr 12.34.56.78 --port 8081 --nofw" );
                        break;
                }
            }

            RouterContext.Inst.ApplyNewSettings();
            Router.Start();

            var destb32 = AppSettings["RemoteDestination"];
            var remotedest = new I2PIdentHash( destb32 );

            MyDestinationInfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            UnpublishedDestination = Router.CreateDestination( MyDestinationInfo, false, out _ );
            UnpublishedDestination.DataReceived += MyDestination_DataReceived;
            UnpublishedDestination.Name = "UnpublishedDestination";

            Logging.LogInformation( $"MyDestination: {UnpublishedDestination.Destination.IdentHash} {MyDestinationInfo.Destination.Certificate}" );

            var interval = new PeriodicAction( TickSpan.Seconds( 40 ) );

            while ( true )
            {
                try
                {
                    Connected = true;

                    while ( Connected )
                    {
                        Thread.Sleep( 2000 );

                        uint recvid = BufUtils.RandomUintNZ();

                        interval.Do( () =>
                        {
                            Logging.LogInformation( $"Program {UnpublishedDestination}: Looking for {remotedest}." );
                            UnpublishedDestination.LookupDestination( remotedest, ( hash, ls, tag ) =>
                            {
                                if ( ls is null )
                                {
                                    Logging.LogInformation( $"Program {UnpublishedDestination}: Failed to lookup {hash.Id32Short}." );
                                    return;
                                }

                                Logging.LogInformation( $"Program {UnpublishedDestination}: Found {remotedest}." );

                                var s = new BufRefStream();
                                var sh = new StreamingPacket(
                                        PacketFlags.SYNCHRONIZE
                                        | PacketFlags.FROM_INCLUDED
                                        | PacketFlags.SIGNATURE_INCLUDED
                                        | PacketFlags.MAX_PACKET_SIZE_INCLUDED )
                                {
                                    From = UnpublishedDestination.Destination,
                                    SigningKey = MyDestinationInfo.PrivateSigningKey,
                                    ReceiveStreamId = recvid,
                                    NACKs = new List<uint>(),
                                    Payload = new BufLen( new byte[0] ),
                                };

                                sh.Write( s );
                                var buf = s.ToByteArray();
                                var zipped = LZUtils.BCGZipCompressNew( new BufLen( buf ) );
                                zipped.PokeFlip16( 4353, 4 ); // source port
                                zipped.PokeFlip16( 25, 6 ); // dest port
                                zipped[9] = (byte)PayloadFormat.Streaming; // streaming

                                Logging.LogInformation( $"Program {UnpublishedDestination}: Sending {zipped:20}." );

                                UnpublishedDestination.Send( ls.Destination, zipped );
                            } );
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

        static void MyDestination_DataReceived( ClientDestination dest, BufLen data )
        {
            Logging.LogInformation( $"Program {UnpublishedDestination}: data received {data:20}" );

            var reader = new BufRefLen( data );
            var unzip = LZUtils.BCGZipDecompressNew( (BufLen)reader );
            var packet = new StreamingPacket( (BufRefLen)unzip );

            Logging.LogInformation( $"Program {UnpublishedDestination}: {packet}" );
        }
    }
}
