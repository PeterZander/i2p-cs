using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class AesEGBuildRequestRecord
    {
        public const int Length = 528;

        public BufLen Data;

        // Might be readable, or encrypted
        public BufLen ToPeer16 { get { return new BufLen( Data, 0, 16 ); } }

        public AesEGBuildRequestRecord( BufRef buf )
        {
            Data = buf.ReadBufLen( Length );
        }

        public AesEGBuildRequestRecord( BufLen dest, EGBuildRequestRecord src, BufferedBlockCipher cipher )
        {
            Data = dest;
            cipher.ProcessBytes( src.Data.BaseArray, src.Data.BaseArrayOffset, src.Data.Length, Data.BaseArray, Data.BaseArrayOffset );
        }

        public AesEGBuildRequestRecord( EGBuildRequestRecord src, BufferedBlockCipher cipher )
        {
            Data = new BufLen( cipher.ProcessBytes( src.Data.BaseArray, src.Data.BaseArrayOffset, src.Data.Length ) );
        }

        public void Process( BufferedBlockCipher cipher )
        {
            cipher.ProcessBytes( Data );
        }

        public void Process( CbcBlockCipher cipher )
        {
            cipher.ProcessBytes( Data );
        }

        public override string ToString()
        {
            var result = new StringBuilder();
            result.AppendLine( "EGAesBuildRequestRecord " + BufUtils.ToBase32String( ToPeer16 ) );
            return result.ToString();
        }
    }

}
