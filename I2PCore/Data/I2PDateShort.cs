using System;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PDateShort : I2PType
    {
        public static readonly I2PDateShort Zero = new I2PDateShort( 0 );

        uint DateSeconds;

        private I2PDateShort()
        {
        }

        public I2PDateShort( uint val )
        {
            DateSeconds = val;
        }

        public I2PDateShort( BufRef reader )
        {
            DateSeconds = reader.ReadFlip32();
        }
        public I2PDateShort( I2PDateShort date )
        {
            DateSeconds = date.DateSeconds;
        }

        public I2PDateShort( I2PDate date )
        {
            DateSeconds = (uint)( (ulong)date / 1000 );
        }

        public static readonly DateTime RefDate = new DateTime( 1970, 1, 1 );

        public I2PDateShort( DateTime dt )
        {
            DateSeconds = (uint)( dt - RefDate ).TotalSeconds;
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( BufUtils.Flip32B( DateSeconds ) );
        }

        public static explicit operator DateTime( I2PDateShort ds )
        {
            return RefDate + TimeSpan.FromSeconds( ds.DateSeconds );
        }

        public static explicit operator uint( I2PDateShort ds )
        {
            return ds.DateSeconds;
        }

        public override string ToString()
        {
            return ( RefDate + new TimeSpan( (long)DateSeconds * 10000000 ) ).ToString();
        }
    }
}
