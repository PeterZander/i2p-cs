using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PDestinationInfo
    {
        public readonly I2PPrivateKey PrivateKey;
        public readonly I2PSigningPrivateKey PrivateSigningKey;

        public readonly I2PDestination Destination;

        public I2PDestinationInfo( I2PSigningKey.SigningKeyTypes signkeytype )
        {
            var certificate = new I2PCertificate( signkeytype );

            var keys = I2PPrivateKey.GetNewKeyPair();

            PrivateKey = keys.PrivateKey;
            PrivateSigningKey = new I2PSigningPrivateKey( certificate );

            Destination = new I2PDestination( 
                    keys.PublicKey,
                    new I2PSigningPublicKey( PrivateSigningKey ) );
        }

        public I2PDestinationInfo( BufRef reader )
        {
            Destination = new I2PDestination( reader );
            PrivateKey = new I2PPrivateKey( reader, Destination.Certificate );
            PrivateSigningKey = new I2PSigningPrivateKey( reader, Destination.Certificate );
        }

        public I2PDestinationInfo( string base64 )
                : this( new BufRefLen( FreenetBase64.Decode( base64 ) ) )
        {
        }

        public byte[] ToByteArray()
        {
            return BufUtils.ToByteArray( Destination, PrivateKey, PrivateSigningKey );
        }

        public string ToBase64()
        {
            return FreenetBase64.Encode( new BufLen( ToByteArray() ) );
        }

        public override string ToString()
        {
            return $"{Destination}";
        }
    }
}
