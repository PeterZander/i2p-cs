using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class RefPair<L,R>
    {
        public L Left { get; set; }
        public R Right { get; set; }

        public RefPair( L l, R r ) { Left = l; Right = r; }
    }
}
