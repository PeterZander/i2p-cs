using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PRoutingKey: I2PType
    {
        bool NextDay;
        I2PIdentHash Identity;

        public I2PRoutingKey( I2PIdentHash ident, bool nextday )
        {
            Identity = ident;
            NextDay = nextday;
        }

        BufLen HashCache;
        int HashCacheDay = -1;

        public BufLen Hash
        {
            get
            {
                var cachecopy = HashCache;
                var daynow = DateTime.UtcNow.Day;
                if ( cachecopy != null && HashCacheDay == daynow ) return cachecopy;

                HashCache = new BufLen( I2PHashSHA256.GetHash( Identity.Hash, new BufLen( GenerateDTBuf( NextDay ) ) ) );
                HashCacheDay = daynow;

                return HashCache;
            }
        }

        static byte[] DTBufCache = null;
        static byte[] DTNextBufCache = null;
        static int DTBufCacheDay = -1;

        private static byte[] GenerateDTBuf( bool next )
        {
            var cachecopy = DTBufCache;
            var nextcachecopy = DTNextBufCache;
            var daynow = DateTime.UtcNow.Day;
            if ( cachecopy != null && DTBufCacheDay == daynow ) return next ? nextcachecopy : cachecopy;

            var dtbuf = Encoding.ASCII.GetBytes( String.Format( "{0:yyyyMMdd}", DateTime.UtcNow ) );
            DTBufCache = dtbuf;

            var nextdtbuf = Encoding.ASCII.GetBytes( String.Format( "{0:yyyyMMdd}", DateTime.UtcNow.AddDays( 1 ) ) );
            DTNextBufCache = nextdtbuf;

            DTBufCacheDay = daynow;

            return next ? nextdtbuf : dtbuf;
        }

        public void Write( List<byte> dest )
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            var hc = HashCache != null ? FreenetBase64.Encode( new BufLen( HashCache ) ): "<null>";
            return string.Format( "I2PRoutingKey: HashCacheDay {0}, HashCache: {1}.", HashCacheDay, hc );
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
