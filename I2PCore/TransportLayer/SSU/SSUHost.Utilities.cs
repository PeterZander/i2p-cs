using System;
using System.Collections.Generic;
using System.Net;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SSUHost
    {
        static readonly DateTime SSURefDateTime = new DateTime( 1970, 1, 1 );
        public static uint SSUTime( DateTime dt ) { return (uint)( ( dt - SSURefDateTime ).TotalSeconds ); }
        public static DateTime SSUDateTime( uint sec ) { return SSURefDateTime.AddSeconds( sec ); }

        class EPComparer : IEqualityComparer<IPEndPoint>
        {
            public bool Equals( IPEndPoint x, IPEndPoint y )
            {
                if ( x == null && y == null ) return false;
                if ( x == null || y == null ) return false;
                if ( ReferenceEquals( x, y ) ) return true;
                return ( BufUtils.Equal( x.Address.GetAddressBytes(), y.Address.GetAddressBytes() ) && x.Port == y.Port );
            }

            public int GetHashCode( IPEndPoint obj )
            {
                return obj.Address.GetAddressBytes().ComputeHash() ^ obj.Port;
            }
        }
    }
}
