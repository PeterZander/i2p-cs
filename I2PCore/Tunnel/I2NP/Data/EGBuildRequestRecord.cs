using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using I2PCore.Router;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class EGBuildRequestRecord: I2PType
    {
        public const int Length = 528;

        public BufLen Data;

        public BufLen ToPeer16 { get { return new BufLen( Data, 0, 16 ); } }
        public BufLen EncryptedData { get { return new BufLen( Data, 16, Length - 16 ); } }

        public EGBuildRequestRecord( BufRef buf )
        {
            Data = buf.ReadBufLen( Length );
        }

        // The AesEGBuildRequestRecord has been decrypted to the degree ToPeer16 is readable.
        public EGBuildRequestRecord( AesEGBuildRequestRecord src )
        {
            Data = src.Data;
        }

        public EGBuildRequestRecord( BufLen dest, BuildRequestRecord src, I2PIdentHash topeer, I2PPublicKey key )
        {
            Data = dest;
            var writer = new BufRefLen( Data );

            writer.Write( topeer.Hash16 );

            var datastart = new BufLen( writer );
            var crypto = new ElGamalCrypto( key );
            crypto.Encrypt( writer, src.Data, false );
        }

        public EGBuildRequestRecord( BuildRequestRecord src, I2PIdentHash topeer, I2PPublicKey key ):
            this( new BufLen( new byte[Length] ), src, topeer, key )
        {
        }

        public BuildRequestRecord Decrypt( I2PPrivateKey pkey )
        {
            return new BuildRequestRecord( new BufRef( ElGamalCrypto.Decrypt( EncryptedData, pkey, false ) ) );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "EGBuildRequestRecord" );
            result.AppendLine( "ToPeer16     : " + BufUtils.ToBase32String( ToPeer16 ) );

            return result.ToString();
        }

        void I2PType.Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }
    }

}
