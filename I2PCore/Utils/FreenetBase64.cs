using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public static class FreenetBase64
    {
	    public static char[] Domain = new char[] { 
		           'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H',
		           'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P',
		           'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X',
		           'Y', 'Z', 'a', 'b', 'c', 'd', 'e', 'f',
		           'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n',
		           'o', 'p', 'q', 'r', 's', 't', 'u', 'v',
		           'w', 'x', 'y', 'z', '0', '1', '2', '3',
		           '4', '5', '6', '7', '8', '9', '-', '~'
	    };

        public static int[] Codomain = null;

	    public static string Encode( BufLen data )
	    {
            var result = new StringBuilder();
            var reader = new BufRefLen( data );

            byte v1, v2;

            for ( int i = 0; i < data.Length / 3; ++i )
            {
                v1 = reader.Read8();
                v2 = (byte)( ( v1 << 4 ) & 0x30 );
                v1 >>= 2;
                result.Append( Domain[v1] );
                v1 = reader.Read8();
                v2 |= (byte)( v1 >> 4 );
                result.Append( Domain[v2] );
                v1 = (byte)( ( v1 & 0x0f ) << 2 );
                v2 = reader.Read8();
                v1 |= (byte)( v2 >> 6 );
                result.Append( Domain[v1] );
                v2 &= 0x3f;
                result.Append( Domain[v2] );
            }

            switch ( data.Length % 3 )
            {
                case 1:
                    v1 = reader.Read8();
                    v2 = (byte)( ( v1 << 4 ) & 0x3f );
                    v1 >>= 2;
                    result.Append( Domain[v1] );
                    result.Append( Domain[v2] );
                    result.Append( "==" );
                    break;

                case 2:
                    v1 = reader.Read8();
                    v2 = (byte)( ( v1 << 4 ) & 0x3f );
                    v1 >>= 2;
                    result.Append( Domain[v1] );
                    v1 = reader.Read8();
                    v2 |= (byte)( v1 >> 4 );
                    result.Append( Domain[v2] );
                    v1 = (byte)( ( v1 & 0x0f ) << 2 );
                    result.Append( Domain[v1] );
                    result.Append( "=" );
                    break;
            }

            return result.ToString();
	    }

        public static byte[] Decode( string data )
        {
            if ( data.Length < 4 || data.Length % 4 != 0 ) throw new FormatException( "FreenetBase64 string needs to be padded to 4 byte align!" );
            var size = 3 * ( data.Length / 4 );
            if ( data[data.Length - 1] == '=' ) --size;
            if ( data[data.Length - 2] == '=' ) --size;
            if ( data[data.Length - 3] == '=' ) --size;
            var result = new byte[size];

            if ( Codomain == null )
            {
                Codomain = new int[256];
                for ( int i = 0; i < 256; ++i ) Codomain[i] = -1;
                for ( int i = 0; i < 64; ++i ) Codomain[(byte)Domain[i]] = i;
                Codomain[(byte)'='] = 0;
            }

            byte v1, v2;
            var reader = data.GetEnumerator();
            reader.MoveNext();
            var writer = new BufRefLen( result );

            for ( int i = 0; i < data.Length / 4; ++i )
            {
                v1 = Lookup( reader );
                v2 = Lookup( reader );
                v1 <<= 2;
                v1 |= (byte)( v2 >> 4 );
                writer.Write8( v1 );
                if ( writer.Length == 0 ) break;
                v2 <<= 4;
                v1 = Lookup( reader );
                v2 |= (byte)( v1 >> 2 );
                writer.Write8( v2 );
                if ( writer.Length == 0 ) break;
                v2 = Lookup( reader );
                v2 |= (byte)( v1 << 6 );
                writer.Write8( v2 );
                if ( writer.Length == 0 ) break;
            }

            return result;
        }

        private static byte Lookup( CharEnumerator reader )
        {
            var v = Codomain[reader.Current]; reader.MoveNext();
            if ( v == -1 ) throw new FormatException( "Unknown Freenet Base64 character '" + reader.Current + "'" );
            return (byte)v;
        }
    }
}
