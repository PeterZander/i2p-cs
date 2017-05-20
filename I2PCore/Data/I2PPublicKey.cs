using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using System.IO;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PPublicKey : I2PKeyType
    {
        public I2PPublicKey( I2PPrivateKey priv ): base( priv.Certificate )
        {
            Key = new BufLen( I2PConstants.ElGamalG.ModPow( priv.ToBigInteger(), I2PConstants.ElGamalP ).ToByteArrayUnsigned() );
        }

        public I2PPublicKey( BufRef buf, I2PCertificate cert ) : base( buf, cert ) { }

        public I2PPublicKey( BigInteger pubkey, I2PCertificate cert ): base( cert )
        {
            Key = new BufLen( pubkey.ToByteArrayUnsigned() );
        }

        public override int KeySizeBytes { get { return Certificate.PublicKeyLength; } }
    }
}
