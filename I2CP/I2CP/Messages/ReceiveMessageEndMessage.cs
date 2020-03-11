using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class ReceiveMessageEndMessage: I2CPMessage
    {
        public ushort SessionId;
        public uint MessageId;

        public ReceiveMessageEndMessage( ushort sessionid, uint msgid )
            : base( ProtocolMessageType.RecvMessageEnd )
        {
            SessionId = sessionid;
            MessageId = msgid;
        }

        public ReceiveMessageEndMessage( BufRefLen reader )
            : base( ProtocolMessageType.RecvMessageEnd )
        {
            SessionId = reader.ReadFlip16();
            MessageId = reader.ReadFlip32();
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[6];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.WriteFlip32( MessageId );
            dest.Write( header );
        }
    }
}
