using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using System.Collections;

namespace I2PCore.Data
{
    public class I2PMapping: I2PType, IEnumerable<KeyValuePair<I2PString,I2PString>>
    {

        // The Mapping must be sorted by key so that the signature will be validated correctly in the router. 
        public SortedDictionary<I2PString, I2PString> Mappings = new SortedDictionary<I2PString, I2PString>( new I2PStringComparer() );

        public I2PMapping()
        {
        }

        public string this[string key]
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

        public I2PString TryGet( string key )
        {
            return Mappings.TryGetValue( new I2PString( key ), out var result ) ? result : null;
        }

        public string TryGet( string key, string def )
        {
            return Mappings.TryGetValue( new I2PString( key ), out var result ) ? result.ToString() : def;
        }

        public bool TryGet( string key, out I2PString result )
        {
            return Mappings.TryGetValue( new I2PString( key ), out result );
        }

        public bool Contains( string key )
        {
            return Mappings.ContainsKey( new I2PString( key ) );
        }

        public bool ValueContains( string key, string value )
        {
            var val = TryGet( key );
            if ( val == null ) return false;
            return val.ToString().Contains( value );
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

        public void Write( BufRefStream dest )
        {
            var buf = new BufRefStream();

            foreach ( var one in Mappings )
            {
                one.Key.Write( buf );
                buf.Write( (byte)'=' );
                one.Value.Write( buf );
                buf.Write( (byte)';' );
            }

            dest.Write( BufUtils.Flip16B( (ushort)buf.Length ) );
            dest.Write( buf );
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

        public IEnumerator<KeyValuePair<I2PString, I2PString>> GetEnumerator()
        {
            return (IEnumerator<KeyValuePair<I2PString, I2PString>>)Mappings.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Mappings.GetEnumerator();
        }
    }
}
