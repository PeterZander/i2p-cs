using System;
using I2PCore.Utils;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using I2PCore.Data;
using Org.BouncyCastle.Utilities.Zlib;
using System.IO.Compression;
using System.Linq;
using System.IO;

namespace I2PCore
{
    public class Bootstrap
    {
        public static readonly string[] DefaultBootstrapUrls =
        {
            "https://reseed.i2p-projekt.de/i2pseeds.su3",
            "https://i2p.mooo.com/netDb/i2pseeds.su3",
            "https://netdb.i2p2.no/i2pseeds.su3",
            "https://download.xxlspeed.com/i2pseeds.su3",
            "https://reseed-fr.i2pd.xyz/i2pseeds.su3",
            "https://reseed.memcpy.io/i2pseeds.su3",
            "https://reseed.onion.im/i2pseeds.su3",
            "https://i2pseed.creativecowpat.net:8443/i2pseeds.su3",
            "https://i2p.novg.net/i2pseeds.su3"
        };

        static bool NoCheckServerCert( 
            object sender, 
            System.Security.Cryptography.X509Certificates.X509Certificate certificate, 
            System.Security.Cryptography.X509Certificates.X509Chain chain, 
            System.Net.Security.SslPolicyErrors sslPolicyErrors )
        {
            return true;
        }

        public static async Task<int> NetworkBootstrap()
        {
            ServicePointManager.ServerCertificateValidationCallback += NoCheckServerCert;
            try
            {
                Logging.LogInformation( "NetworkBootstrap: Trying to bootstrap from network." );

                var ( url, su3 ) = await GetSU3FromRandomHost();

                if ( su3 is null )
                {
                    Logging.LogInformation( "NetworkBootstrap: Retrieving routers from network seeds failed." );
                    return 0;
                }

                try
                {
                    var importcount = ImportSU3File( new BufLen( su3 ) );
                    Logging.LogInformation( $"Bootstrap: {importcount} files imported from '{url}'." );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback -= NoCheckServerCert;
            }

            return 0;
        }

        public static int FileBootstrap( string filename )
        {
            try
            {
                var data = new BufLen( File.ReadAllBytes( filename ) );
                var importcount = ImportSU3File( data );

                Logging.LogInformation( $"Bootstrap: {importcount} files imported from '{filename}'." );
                return importcount;
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
                return 0;
            }
        }

        private static int ImportSU3File( BufLen data )
        {
            var importcount = 0;
            using ( var arch = GetRouterInfoFiles( data ) )
            {
                foreach ( var file in arch.Entries )
                {
                    using ( var s = file.Open() )
                    {
                        if ( NetDb.Inst.AddRouterInfo( s ) ) ++importcount;
                    }
                }
            }

            return importcount;
        }

        private static ZipArchive GetRouterInfoFiles( BufLen data )
        {
            try
            {
                var reader = new BufRefLen( data );
                var header = new I2PSU3Header( reader );

                if ( header.FileType != I2PSU3Header.SU3FileTypes.Zip )
                {
                    throw new ArgumentException( $"Unknown FileType in SU3: {header.FileType}" );
                }

                if ( header.ContentType != I2PSU3Header.SU3ContentTypes.SeedData )
                {
                    throw new ArgumentException( $"Unknown ContentType in SU3: {header.ContentType}" );
                }

                // TODO: Verify signature

                var s = new BufRefStream();
                s.Write( reader );

                return new ZipArchive( s );
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }

            return null;
        }

        public static async Task<(string url,byte[])> GetSU3FromRandomHost()
        {
            var localhosts = new HashSet<string>( DefaultBootstrapUrls );
            var maxretries = 10;

            while ( localhosts.Any() && maxretries-- > 0 )
            {
                var host = localhosts.Random();
                if ( host is null ) continue;

                try
                {
                    using ( var client = new HttpClient() )
                    {
                        client.DefaultRequestHeaders.ConnectionClose = true;
                        client.DefaultRequestHeaders.Add(
                            "User-Agent",
                            "Wget/1.11.4" );

                        var getresult = await client.GetAsync( host );

                        if ( getresult.StatusCode != HttpStatusCode.OK )
                        {
                            Logging.LogInformation( $"NetworkBootstrap: Failed to " +
                                $"get reseed info from {host}. Status {getresult.StatusCode}." );
                            continue;
                        }

                        var result = await getresult.Content.ReadAsByteArrayAsync();
                        return ( host, result );
                    }
                }
                catch ( Exception ex )
                {
                    localhosts.Remove( host );
                    Logging.Log( ex );
                }
            }

            return ( null, null );
        }
    }
}
