using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class DestLookupMessage: I2CPMessage
    {
        public I2PIdentHash Ident;

        public DestLookupMessage( I2PIdentHash hash )
            : base( ProtocolMessageType.DestLookup )
        {
            Ident = hash;
        }

        public DestLookupMessage( BufRefLen reader )
            : base( ProtocolMessageType.DestLookup )
        {
            Ident = new I2PIdentHash( reader );
        }

        public override void Write( BufRefStream dest )
        {
            Ident.Write( dest );
        }
    }
}
