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
        public readonly I2PPublicKey PublicKey;

        public readonly I2PSigningPrivateKey PrivateSigningKey;
        public readonly I2PSigningPublicKey PublicSigningKey;

        public readonly I2PCertificate Certificate;

        public I2PDestinationInfo( I2PSigningKey.SigningKeyTypes signkeytype )
        {
            Certificate = new I2PCertificate( signkeytype );
            var keys = I2PPrivateKey.GetNewKeyPair();
            PublicKey = keys.PublicKey;
            PrivateKey = keys.PrivateKey;

            PrivateSigningKey = new I2PSigningPrivateKey( Certificate );
            PublicSigningKey = new I2PSigningPublicKey( PrivateSigningKey );
        }

        public I2PDestinationInfo( 
            I2PCertificate cert,
            I2PPublicKey pubkey,
            I2PSigningPublicKey spubkey,
            I2PPrivateKey privkey,
            I2PSigningPrivateKey sprivkey )
        {
            Certificate = cert;
            PublicKey = pubkey;
            PrivateKey = privkey;
            PrivateSigningKey = sprivkey;
            PublicSigningKey = spubkey;
        }

        public I2PDestinationInfo( BufRef reader )
        {
            Certificate = new I2PCertificate( reader );
            PrivateKey = new I2PPrivateKey( reader, Certificate );
            PublicKey = new I2PPublicKey( reader, Certificate );
            PrivateSigningKey = new I2PSigningPrivateKey( reader, Certificate );
            PublicSigningKey = new I2PSigningPublicKey( reader, Certificate );
        }

        public I2PDestinationInfo( string base64 )
        {
            var reader = new BufRefLen( FreenetBase64.Decode( base64 ) );

            Certificate = new I2PCertificate( reader );
            PrivateKey = new I2PPrivateKey( reader, Certificate );
            PublicKey = new I2PPublicKey( reader, Certificate );
            PrivateSigningKey = new I2PSigningPrivateKey( reader, Certificate );
            PublicSigningKey = new I2PSigningPublicKey( reader, Certificate );
        }

        public byte[] ToByteArray()
        {
            return BufUtils.ToByteArray( Certificate, PrivateKey, PublicKey, PrivateSigningKey, PublicSigningKey );
        }

        public string ToBase64()
        {
            return FreenetBase64.Encode( new BufLen( ToByteArray() ) );
        }

        public override string ToString()
        {
            return $"{Certificate} {PublicKey}";
        }
    }
}
