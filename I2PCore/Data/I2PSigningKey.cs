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
        public static int SigningPublicKeyLength( I2PSigningKey.SigningKeyTypes skt )
        {
            switch ( skt )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    return 128;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    return 65;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    return 97;

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    return 32;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    return 133;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                    return 256;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                    return 384;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                    return 512;

                default:
                    throw new NotImplementedException();
            }
        }
        public static int SigningPrivateKeyLength( I2PSigningKey.SigningKeyTypes skt )
        {
            switch ( skt )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    return 20;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    return 32;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    return 48;

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    return 32;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    return 66;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                    return 512;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                    return 768;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                    return 1024;

                default:
                    throw new NotImplementedException();
            }
        }
        public static int SignatureLength( I2PSigningKey.SigningKeyTypes skt )
        {
            switch ( skt )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    return 40;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    return 64;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    return 96;

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    return 64;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    return 132;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                    return 256;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                    return 384;

                case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                    return 512;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
