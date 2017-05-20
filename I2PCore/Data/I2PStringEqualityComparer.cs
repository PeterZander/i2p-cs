using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Data
{
    class I2PStringEqualityComparer: IEqualityComparer<I2PString>
    {
        bool IEqualityComparer<I2PString>.Equals( I2PString x, I2PString y )
        {
            return x.Equals( y );
        }

        int IEqualityComparer<I2PString>.GetHashCode( I2PString obj )
        {
            return obj.GetHashCode();
        }
    }
}
