using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PMapping: I2PType
    {

        // The Mapping must be sorted by key so that the signature will be validated correctly in the router. 
        public SortedDictionary<I2PString, I2PString> Mappings = new SortedDictionary<I2PString, I2PString>( new I2PStringComparer() );

        public I2PMapping()
        {
        }

        public string this [string key]
        {
            set
            {
                Mappings[new I2PString( key )] = new I2PString( value );
            }
            get
            {
                return Mappings[new I2PString( key )].ToString();
            }
        }

        public bool Contains( string key )
        {
            return Mappings.ContainsKey( new I2PString( key ) );
        }

        public I2PMapping( BufRef buf )
        {
            var bytes = buf.ReadFlip16();
            var endpos = buf.BaseArrayOffset + bytes;

            while ( buf.BaseArrayOffset < endpos )
            {
                var key = new I2PString( buf );
                if ( buf.Peek8( 0 ) == '=' ) buf.Seek( 1 );
                var value = new I2PString( buf );
                if ( buf.Peek8( 0 ) == ';' ) buf.Seek( 1 );
                Mappings[key] = value;
            }
        }

        public void Write( List<byte> dest )
        {
            var buf = new List<byte>();

            foreach ( var one in Mappings )
            {
                one.Key.Write( buf );
                buf.Add( (byte)'=' );
                one.Value.Write( buf );
                buf.Add( (byte)';' );
            }

            dest.AddRange( BitConverter.GetBytes( BufUtils.Flip16( (ushort)buf.Count ) ) );
            dest.AddRange( buf );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.Append( "I2PMapping: Pairs: ->" );
            foreach ( var one in Mappings )
            {
                result.AppendFormat( "({0}:{1})", one.Key, one.Value );
            }
            result.Append( "<-" );

            return result.ToString();
        }
    }
}
