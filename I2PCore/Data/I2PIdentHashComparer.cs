using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PIdentHashComparer: IEqualityComparer<I2PIdentHash>
    {
        public bool Equals( I2PIdentHash x, I2PIdentHash y )
        {
            return BufUtils.Equals( x.Hash, y.Hash );
        }

        public int GetHashCode( I2PIdentHash x )
        {
            return x.GetHashCode();
        }
    }
}
