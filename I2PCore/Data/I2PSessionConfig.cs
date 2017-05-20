using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using Org.BouncyCastle.Math;

namespace I2PCore.Data
{
    public class I2PSessionConfig: I2PType
    {
        I2PDestination Destination;
        I2PMapping Map;
        I2PDate Date;
        I2PSigningPrivateKey PrivateSigningKey;

        public I2PSessionConfig( I2PDestination dest, I2PMapping map, I2PDate date, I2PSigningPrivateKey privsignkey )
        {
            Destination = dest;
            Map = map != null ? map : new I2PMapping();
            Date = date != null ? date : new I2PDate( DateTime.Now );
            PrivateSigningKey = privsignkey;
        }

        public void Write( List<byte> dest )
        {
            var dest2 = new List<byte>();
            Destination.Write( dest2 );
            Map.Write( dest2 );
            Date.Write( dest2 );
            var dest2data = dest2.ToArray();

            var sig = new I2PSignature( new BufRefLen( I2PSignature.DoSign( PrivateSigningKey, new BufLen( dest2data ) ) ), PrivateSigningKey.Certificate );

            dest.AddRange( dest2data );
            sig.Write( dest );
        }
    }
}
