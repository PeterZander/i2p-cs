
using System;
using I2PCore.Utils;
using static I2PCore.Data.I2PSigningKey;

namespace I2PCore.Data
{
    public class I2POfflineSignature: I2PType
    {
        public I2PDateShort Expires { get; set; }
        public SigningKeyTypes SignatureType { get; set; }
        public I2PSigningPublicKey TransientPublicKey { get; set; }
        public I2PSignature Signature { get; set; }
        public I2POfflineSignature( BufRef reader, I2PCertificate cert )
        {
            Expires = new I2PDateShort( reader.ReadFlip32() );
            SignatureType = (SigningKeyTypes)reader.ReadFlip16();

            TransientPublicKey = new I2PSigningPublicKey( 
                reader, 
                new I2PCertificate( SignatureType ) );

            Signature = new I2PSignature( reader, cert );
        }
        public void Write( BufRefStream dest )
        {
            Expires.Write( dest );
            dest.Write( BufUtils.Flip16B( (ushort)SignatureType ) );
            TransientPublicKey.Write( dest );
            Signature.Write( dest );
        }
    }
}
