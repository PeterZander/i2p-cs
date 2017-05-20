using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
    public class RouterUnresolvableException: Exception
    {
        public RouterUnresolvableException() : base() { }
        public RouterUnresolvableException( string text ) : base( text ) { }
    }
}
