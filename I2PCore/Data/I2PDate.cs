using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.IO;

namespace I2PCore.Data
{
    public class I2PDate : I2PType
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

        public I2PDate( I2PDate date )
        {
            DateMilliseconds = date.DateMilliseconds;
        }

        public static readonly DateTime RefDate = new DateTime( 1970, 1, 1 );

        public I2PDate( DateTime dt )
        {
            DateMilliseconds = (UInt64)( dt - RefDate ).TotalMilliseconds;
        }

        public I2PDate( BufRef buf )
        {
            DateMilliseconds = buf.ReadFlip64();
        }

        public void Write( List<byte> dest )
        {
            dest.AddRange( BufUtils.Flip64B( DateMilliseconds ) );
        }

        public void Write( BufRef dest )
        {
            dest.WriteFlip64( DateMilliseconds );
        }

        public void Poke( BufBase dest, int offset )
        {
            dest.PokeFlip64( DateMilliseconds, offset );
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

        public static I2PDate DefaultI2NPExpiration()
        {
            // Ref impl uses 1 minute DEFAULT_EXPIRATION_MS in I2NPMessageImpl.java
            return new I2PDate( DateTime.UtcNow.AddMinutes( 1 ) );

            // From I2Pd I2NPProtocol.h I2NP_MESSAGE_EXPIRATION_TIMEOUT
            //return new I2PDate( DateTime.UtcNow.AddSeconds( 8 ) ); 
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
