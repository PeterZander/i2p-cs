using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
    public class FailedToConnectException: Exception
    {
        public FailedToConnectException( string text ) : base( text ) { }
    }
}
