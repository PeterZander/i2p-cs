using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2P.I2CP;
using System.Net.Sockets;
using System.IO;
using I2PCore.Tunnel.I2NP;
using System.Threading;
using I2PCore.Utils;
using I2PCore.Router;
using I2PCore.Transport.NTCP;
using System.Net;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore;
using I2PCore.Transport;
using I2PCore.Tunnel;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Transport.SSU;

namespace I2P
{
    class Program
    {
        static void Main( string[] args )
        {
            PeriodicAction LsLookup = new PeriodicAction( TickSpan.Minutes( 5 ) ); 
            
            DebugUtils.LogToConsole = true;
            DebugUtils.LogToFile( "i2p.log" );
            DebugUtils.LogInformation( "Me: " + RouterContext.Inst.MyRouterIdentity.IdentHash.Id32 );

            RouterContext.Inst.DefaultExtAddress = IPAddress.Parse( "52.84.212.112" );
            RouterContext.Inst.DefaultTCPPort = 32328;
            RouterContext.Inst.DefaultUDPPort = 32328;
            RouterContext.Inst.ApplyNewSettings();

            var pnp = new UPnp();
            Thread.Sleep( 5000 ); // Give UPnp a chance

            Router.Start();

            while ( true )
            {
                try
                {
                    Connected = true;

                    var dest = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
                    var mydest = Router.CreateDestination( dest, false );
                    mydest.DataReceived += new ClientDestination.DestinationDataReceived( mydest_DataReceived );

                    var hashes = new I2PIdentHash[] {
                        new I2PIdentHash( "udhdrtrcetjm5sxaskjyr5ztseszydbh4dpl3pl4utgqqw2v4jna.b32.i2p" ),
                        new I2PIdentHash( "7tbay5p4kzemkxvyvbf6v7eau3zemsnnl2aoyqhg5jzpr5eke7tq.b32.i2p" ),
                        new I2PIdentHash( "ukeu3k5oycga3uneqgtnvselmt4yemvoilkln7jpvafvfx7dnkdq.b32.i2p" )
                    };

                    while ( Connected )
                    {
                        Thread.Sleep( 20000 );
                        /*
                        if ( LS != null )
                        {
                            mydest.Send( LS, BufUtils.Random( 200 ), false );
                        }*/
                        LsLookup.Do( () => mydest.LookupDestination( hashes.Random(), new ClientDestination.DestinationLookupResult( LookupResult ) ) );
                    }
                }
                catch ( SocketException ex )
                {
                    DebugUtils.Log( ex );
                }
                catch ( IOException ex )
                {
                    DebugUtils.Log( ex );
                }
                catch ( Exception ex )
                {
                    DebugUtils.Log( ex );
                }
            }
        }

        static void mydest_DataReceived( BufLen data )
        {
        }

        static I2PLeaseSet LS = null;
        static void LookupResult( I2PIdentHash hash, I2PLeaseSet ls )
        {
            LS = ls;
        }

        static bool Connected = false;
    }
}
