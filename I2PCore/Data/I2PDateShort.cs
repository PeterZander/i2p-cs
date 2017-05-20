using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.IO;

namespace I2PCore.Data
{
    public class I2PDateShort : I2PType
    {
        public static readonly I2PDateShort Zero = new I2PDateShort( 0 );

        uint DateSeconds;

        private I2PDateShort()
        {
        }

        private I2PDateShort( uint val )
        {
            DateSeconds = val;
        }

        public I2PDateShort( I2PDateShort date )
        {
            DateSeconds = date.DateSeconds;
        }

        public static readonly DateTime RefDate = new DateTime( 1970, 1, 1 );

        public I2PDateShort( DateTime dt )
        {
            DateSeconds = (uint)( dt - RefDate ).TotalSeconds;
        }

        public void Write( List<byte> dest )
        {
            dest.AddRange( BufUtils.Flip64B( DateSeconds ) );
        }

        public override string ToString()
        {
            return ( RefDate + new TimeSpan( (long)DateSeconds * 10000000 ) ).ToString();
        }
    }
}
