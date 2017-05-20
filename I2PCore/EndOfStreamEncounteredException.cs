using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
    public class EndOfStreamEncounteredException: Exception
    {
        public EndOfStreamEncounteredException() : base() { }
        public EndOfStreamEncounteredException( string text ) : base( text ) { }
    }
}
