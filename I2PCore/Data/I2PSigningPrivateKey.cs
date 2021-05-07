using Org.BouncyCastle.Math;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSigningPrivateKey : I2PSigningKey
    {
        public override int KeySizeBytes { get { return Certificate.SigningPrivateKeyLength; } }

        public I2PSigningPrivateKey( I2PCertificate cert ) 
            : base( new BufLen( BufUtils.RandomBytes( cert.SigningPrivateKeyLength ) ), cert ) 
        {
        }

        public I2PSigningPrivateKey( BufRef reader, I2PCertificate cert ) : base( reader, cert ) 
        {
        }

        public I2PSigningPrivateKey( BigInteger key, I2PCertificate cert ) : base( key, cert ) 
        {
        }
    }
}
