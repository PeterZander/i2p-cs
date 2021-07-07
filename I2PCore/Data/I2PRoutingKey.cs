using System;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PRoutingKey: I2PType
    {
        private readonly I2PIdentHash Identity;

        public I2PRoutingKey( I2PIdentHash ident )
        {
            Identity = ident;
        }

        BufLen HashCache;
        DateTime HashCacheDay = DateTime.MinValue;

        public BufLen Hash
        {
            get
            {
                var daynow = DateTime.UtcNow.Date;
                if ( HashCache != null && HashCacheDay == daynow ) return HashCache;

                HashCache = new BufLen( I2PHashSHA256.GetHash( Identity.Hash, new BufLen( GenerateDTBuf() ) ) );
                HashCacheDay = daynow;

                return HashCache;
            }
        }

        static byte[] DTBufCache = null;
        static DateTime DTBufCacheDay = DateTime.MinValue;

        private static byte[] GenerateDTBuf()
        {
            var daynow = DateTime.UtcNow.Date;
            if ( DTBufCache != null && DTBufCacheDay == daynow ) return DTBufCache;

            DTBufCache = Encoding.ASCII.GetBytes( $"{daynow:yyyyMMdd}" );
            DTBufCacheDay = daynow;

            return DTBufCache;
        }

        public void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            var hc = HashCache != null ? FreenetBase64.Encode( new BufLen( HashCache ) ): "<null>";
            return $"I2PRoutingKey: HashCacheDay {HashCacheDay}, HashCache: {hc}.";
        }

        public byte this[int ix]
        {
            get { return Hash[ix]; }
        }


        /// <summary>
        /// Distance definitions is always between as stored floodfill IdentHash and a searched RoutingKey.
        /// Not between two routing keys.
        /// </summary>
        /// <returns></returns>
        public static BufLen operator ^( I2PIdentHash left, I2PRoutingKey right )
        {
            var result = new byte[32];
            var lhash = left.Hash;
            var rhash = right.Hash;
            for ( int i = 0; i < 32; ++i )
            {
                result[i] = (byte)( lhash[i] ^ rhash[i] );
            }
            return new BufLen( result );
        }
    }
}
