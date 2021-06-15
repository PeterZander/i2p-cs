using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.IO;

namespace I2PCore.Data
{
    public class I2PDate : I2PType, IComparable, IComparable<I2PDate>
    {
        public static readonly I2PDate Zero = new I2PDate( 0 );

        ulong DateMilliseconds;

        private I2PDate()
        {
        }

        /// <summary>
        /// Set value explicitly.
        /// </summary>
        /// <param name="val">Milliseconds since Jan 1st 1970.</param>
        public I2PDate( UInt64 val )
        {
            DateMilliseconds = val;
        }
        public I2PDate( BufRef reader )
        {
            DateMilliseconds = reader.ReadFlip64();
        }
        public I2PDate( I2PDate date )
        {
            DateMilliseconds = date.DateMilliseconds;
        }

        public static readonly DateTime RefDate = new DateTime( 1970, 1, 1 );

        public I2PDate( DateTime dt )
        {
            DateMilliseconds = (UInt64)( dt - RefDate ).TotalMilliseconds;
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( BufUtils.Flip64B( DateMilliseconds ) );
        }

        public void Write( BufRef dest )
        {
            dest.WriteFlip64( DateMilliseconds );
        }

        public void Poke( BufBase dest, int offset )
        {
            dest.PokeFlip64( DateMilliseconds, offset );
        }

        public ulong Nudge()
        {
            return ++DateMilliseconds;
        }

        public override string ToString()
        {
            return ( RefDate + new TimeSpan( (long)DateMilliseconds * 10000 ) ).ToString();
        }

        public static explicit operator DateTime( I2PDate date )
        {
            return RefDate + TimeSpan.FromMilliseconds( date.DateMilliseconds );
        }

        public static explicit operator ulong( I2PDate date )
        {
            return date.DateMilliseconds;
        }

        public int CompareTo( object obj )
        {
            if ( obj is null ) return 1;
            var other = obj as I2PDate;
            if ( other is null ) return 1;
            if ( DateMilliseconds == other.DateMilliseconds ) return 0;
            return DateMilliseconds > other.DateMilliseconds ? 1 : -1;
        }

        public int CompareTo( I2PDate other )
        {
            if ( other is null ) return 1;
            if ( DateMilliseconds == other.DateMilliseconds ) return 0;
            return DateMilliseconds > other.DateMilliseconds ? 1 : -1;
        }

        public static I2PDate Now
        {
            get
            {
                return new I2PDate( DateTime.UtcNow );
            }
        }
    }
}
