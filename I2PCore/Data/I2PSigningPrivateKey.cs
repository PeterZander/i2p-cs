using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSigningPrivateKey : I2PSigningKey
    {
        public override int KeySizeBytes { get { return Certificate.SigningPrivateKeyLength; } }

        public I2PSigningPrivateKey( I2PCertificate cert ) 
            : base( new BufLen( BufUtils.RandomBytes( cert.SigningPrivateKeyLength ) ), cert ) 
        {
            if ( cert.SignatureType == SigningKeyTypes.EdDSA_SHA512_Ed25519 )
            {
                ExpandedPrivateKey = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed( ToByteArray() );
            }
        }

        public I2PSigningPrivateKey( BufRef reader, I2PCertificate cert ) : base( reader, cert ) 
        {
            if ( cert.SignatureType == SigningKeyTypes.EdDSA_SHA512_Ed25519 )
            {
                ExpandedPrivateKey = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed( ToByteArray() );
            }
        }

        public I2PSigningPrivateKey( BigInteger key, I2PCertificate cert ) : base( key, cert ) 
        {
            if ( cert.SignatureType == SigningKeyTypes.EdDSA_SHA512_Ed25519 )
            {
                ExpandedPrivateKey = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed( ToByteArray() );
            }
        }

        public byte[] ExpandedPrivateKey;
    }
}
