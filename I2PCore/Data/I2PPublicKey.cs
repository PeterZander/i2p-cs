using Org.BouncyCastle.Math;
using I2PCore.Utils;
using System;

namespace I2PCore.Data
{
    public class I2PPublicKey : I2PKeyType
    {
        public I2PPublicKey( I2PPrivateKey priv ): base( priv.Certificate )
        {
            switch( Certificate.PublicKeyType )
            {
                case KeyTypes.ElGamal2048:
                    Key = new BufLen( I2PConstants
                            .ElGamalG.ModPow( 
                                priv.ToBigInteger(), 
                                I2PConstants.ElGamalP )
                                    .ToByteArrayUnsigned() );
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public I2PPublicKey( BufRef buf, I2PCertificate cert ) : base( buf, cert ) { }

        public I2PPublicKey( BigInteger pubkey, I2PCertificate cert ): base( cert )
        {
            Key = new BufLen( pubkey.ToByteArrayUnsigned() );
        }

        public override int KeySizeBytes { get { return Certificate.PublicKeyLength; } }
    }
}
