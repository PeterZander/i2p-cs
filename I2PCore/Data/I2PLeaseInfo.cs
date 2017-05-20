using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace I2PCore.Data
{
    public class I2PLeaseInfo
    {
        public I2PPrivateKey PrivateKey;
        public I2PPublicKey PublicKey;

        public I2PSigningPrivateKey PrivateSigningKey;
        public I2PSigningPublicKey PublicSigningKey;

        public I2PLeaseInfo( 
            I2PPublicKey pubkey,
            I2PSigningPublicKey spubkey,
            I2PPrivateKey privkey,
            I2PSigningPrivateKey sprivkey )
        {
            PublicKey = pubkey;
            PrivateKey = privkey;
            PrivateSigningKey = sprivkey;
            PublicSigningKey = spubkey;
        }

        public I2PLeaseInfo( I2PDestinationInfo di )
        {
            PublicKey = di.PublicKey;
            PrivateKey = di.PrivateKey;
            PrivateSigningKey = di.PrivateSigningKey;
            PublicSigningKey = di.PublicSigningKey;
        }
    }
}