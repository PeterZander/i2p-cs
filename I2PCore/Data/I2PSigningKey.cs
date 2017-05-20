using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public abstract class I2PSigningKey : I2PKeyType
    {
        public enum SigningKeyTypes : ushort
        {
            Invalid = ushort.MaxValue,
            DSA_SHA1 = 0,
            ECDSA_SHA256_P256 = 1,
            ECDSA_SHA384_P384 = 2,
            ECDSA_SHA512_P521 = 3,
            RSA_SHA256_2048 = 4,
            RSA_SHA384_3072 = 5,
            RSA_SHA512_4096 = 6,
            EdDSA_SHA512_Ed25519 = 7
        }

        protected I2PSigningKey( I2PCertificate cert ): base( cert )
        {
        }

        public I2PSigningKey( BufRef reader, I2PCertificate cert ) : base( reader, cert ) 
        {
        }

        public I2PSigningKey( BigInteger key, I2PCertificate cert ) : base( cert )
        {
            var buf = key.ToByteArrayUnsigned();
            if ( buf.Length == KeySizeBytes )
            {
                Key = new BufLen( buf );
            }
            else
            {
                Key = new BufLen( new byte[KeySizeBytes] );
                Key.Poke( buf, KeySizeBytes - buf.Length );
            }
        }

        public I2PSigningKey( BufLen key, I2PCertificate cert )
            : base( cert )
        {
            if ( key.Length == KeySizeBytes )
            {
                Key = key;
            }
            else if ( key.Length < KeySizeBytes )
            {
                Key = new BufLen( new byte[KeySizeBytes] );
                Key.Poke( key, KeySizeBytes - key.Length );
            }
            else
            {
                Key = new BufLen( key, 0, KeySizeBytes );
            }
        }
    }
}
