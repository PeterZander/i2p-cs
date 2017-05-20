using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
	public class ChecksumFailureException: Exception
	{
        public ChecksumFailureException() : base() { }
        public ChecksumFailureException( string msg ) : base( msg ) { }
	}
}
