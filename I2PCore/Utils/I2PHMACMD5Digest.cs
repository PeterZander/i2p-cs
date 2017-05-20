using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.Utilities;

namespace I2PCore.Utils
{
    public class I2PHMACMD5Digest
    {
        const int BLOCK_LENGTH = 32;

        const ulong IPAD = 0x3636363636363636;
        const ulong OPAD = 0x5C5C5C5C5C5C5C5C;
        readonly static byte[] IPADBUF = { 
            0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36,
            0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36,
            0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36,
            0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36, 0x36,
        };
        readonly static byte[] OPADBUF = { 
            0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
            0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
            0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
            0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
        };
        readonly static byte[] NULLPAD = new byte[16];

        public static byte[] Generate( BufLen msg, BufLen key )
        {
            return Generate( new BufLen[] { msg }, key );
        }

        public static byte[] Generate( IEnumerable<BufLen> msg, BufLen key )
        {
            var result = new byte[16];
            Generate( msg, key, new BufLen( result ) );
            return result;
        }

        public static BufLen Generate( IEnumerable<BufLen> msg, BufLen key, BufLen dest )
        {
            if ( key.Length != 32 ) throw new NotImplementedException( "Only keys of 32 bits supported" );

            var m5 = new MD5Digest();
            var hash = new byte[m5.GetDigestSize()];
            var buf = new byte[BLOCK_LENGTH];

            var writer = new BufRefLen( buf );
            writer.Write64( key.Peek64( 0 ) ^ IPAD );
            writer.Write64( key.Peek64( 1 * 8 ) ^ IPAD );
            writer.Write64( key.Peek64( 2 * 8 ) ^ IPAD );
            writer.Write64( key.Peek64( 3 * 8 ) ^ IPAD );

            m5.BlockUpdate( buf, 0, BLOCK_LENGTH );
            m5.BlockUpdate( IPADBUF, 0, IPADBUF.Length );
            foreach ( var one in msg ) m5.BlockUpdate( one.BaseArray, one.BaseArrayOffset, one.Length );
            m5.DoFinal( hash, 0 );

            writer = new BufRefLen( buf );
            writer.Write64( key.Peek64( 0 ) ^ OPAD );
            writer.Write64( key.Peek64( 1 * 8 ) ^ OPAD );
            writer.Write64( key.Peek64( 2 * 8 ) ^ OPAD );
            writer.Write64( key.Peek64( 3 * 8 ) ^ OPAD );

            m5.Reset();
            m5.BlockUpdate( buf, 0, BLOCK_LENGTH );
            m5.BlockUpdate( OPADBUF, 0, OPADBUF.Length );
            m5.BlockUpdate( hash, 0, hash.Length );
            m5.BlockUpdate( NULLPAD, 0, NULLPAD.Length );
            if ( dest.Length < m5.GetDigestSize() ) throw new OverflowException( "Not enough dest buffer size for I2PHMACMD5Digest.Generate()!" );
            m5.DoFinal( dest.BaseArray, dest.BaseArrayOffset );
            return dest;
        }
    }
}
