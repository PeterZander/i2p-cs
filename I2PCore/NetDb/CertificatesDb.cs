using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace I2PCore
{
    public static class CertificatesDb
    {
        private const string DefaultCertificatesDir = "Contrib?Certificates";
        public static string DefaultCertificatesDirectory
        {
            get
            {
                return Path.GetFullPath( DefaultCertificatesDir
                    .Replace( '?', Path.DirectorySeparatorChar ) );
            }
        }

        public static X509Certificate2[] GetCertificates()
        {
            var files = Directory.GetFiles(
                DefaultCertificatesDirectory,
                "*.crt",
                SearchOption.AllDirectories );

            var result = new List<X509Certificate2>();
            foreach( var file in files )
            {
                var cert = new X509Certificate2( file );
                result.Add( cert );
            }
            return result.ToArray();
        }
    }
}
