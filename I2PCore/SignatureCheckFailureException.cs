using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
    public class SignatureCheckFailureException: Exception
    {
        public SignatureCheckFailureException() : base() { }
        public SignatureCheckFailureException( string msg ) : base( msg ) { }
    }
}
