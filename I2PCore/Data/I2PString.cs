using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using System.Collections;

namespace I2PCore.Data
{
    public class I2PString : I2PType
    {
        readonly String Str = "";

        public I2PString()
        {
        }

        public I2PString( I2PString src )
        {
            Str = src.Str;
        }

        public I2PString( String src )
        {
            Str = src;
        }

        public I2PString( Stream src, char[] skipchars )
        {
            Read( src, skipchars );
        }

        public I2PString( BufRef buf )
        {
            var len = buf.Read8();
            Str = System.Text.Encoding.UTF8.GetString( buf.BaseArray, buf.BaseArrayOffset, len );
            buf.Seek( len );
        }

        internal int CompareTo( I2PString other )
        {
            return string.CompareOrdinal( Str, other.Str );
        }

        public byte[] GetBytes
        {
            get
            {
                return System.Text.Encoding.UTF8.GetBytes( Str );
            }
        }

        public void Write( BufRefStream dest )
        {
            var l = (byte)Math.Min( 255, Str.Length );
            var v = new byte[System.Text.Encoding.UTF8.GetMaxByteCount( l ) + 1];
            v[0] = l;
            var bytes = System.Text.Encoding.UTF8.GetBytes( Str, 0, l, v, 1 );
            dest.Write( v, 0, bytes + 1 );
        }

        public void Read( Stream src, char[] skipchars )
        {
            var len = (int)StreamUtils.ReadInt8( src );
            while ( skipchars != null && skipchars.Any( c => len == (long)c ) ) len = (int)StreamUtils.ReadInt8( src );

            var buf = new byte[len];
            var read = src.Read( buf, 0, len );
            if ( read != len ) throw new EndOfStreamEncounteredException();
            System.Text.Encoding.UTF8.GetString( buf );
        }

        public override string ToString()
        {
            return Str ?? "[I2PString]";
        }

        public static bool operator ==( I2PString stl, string str )
        {
            return StringComparer.InvariantCulture.Compare( stl?.Str, str ) == 0;
        }

        public static bool operator !=( I2PString stl, string str )
        {
            return StringComparer.InvariantCulture.Compare( stl?.Str, str ) != 0;
        }

        public override bool Equals( object obj )
        {
            if ( obj is I2PString ) return Str == ( (I2PString)obj )?.Str;
            if ( obj is string ) return this == (string)obj;
            return false;
        }

        public override int GetHashCode()
        {
            return Str.GetHashCode();
        }
    }
}
