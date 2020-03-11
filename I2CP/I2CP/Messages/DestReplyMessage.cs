using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class DestReplyMessage : I2CPMessage
    {
        public I2PDestination Destination;
        public I2PIdentHash Ident;

        // Success
        public DestReplyMessage( I2PDestination dest )
            : base( ProtocolMessageType.DestReply )
        {
            Destination = dest;
        }

        // Failure
        public DestReplyMessage( I2PIdentHash hash )
            : base( ProtocolMessageType.DestReply )
        {
            Ident = hash;
        }

        public DestReplyMessage( BufRefLen reader )
            : base( ProtocolMessageType.DestReply )
        {
            Destination = null;
            Ident = null;

            if ( reader.Length == 0 ) return;
            if ( reader.Length == 32 )
            {
                Ident = new I2PIdentHash( reader );
            }
            else
            {
                Destination = new I2PDestination( reader );
            }
        }

        public override void Write( BufRefStream dest )
        {
            if ( Destination != null )
            {
                Destination.Write( dest );
                return;
            }

            Ident.Write( dest );
        }
    }
}
