using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class MessagePayloadMessage: I2CPMessage
    {
        public ushort SessionId;
        public uint MessageId;
        public BufLen Payload;

        public MessagePayloadMessage( ushort sessionid, uint msgid, BufLen data )
            : base( ProtocolMessageType.MessagePayload )
        {
            SessionId = sessionid;
            MessageId = msgid;
            Payload = data;
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[10];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.WriteFlip32( MessageId );
            writer.WriteFlip32( (uint)Payload.Length );

            dest.Write( header );
            dest.Write( (BufRefLen)Payload );
        }

        public override string ToString()
        {
            return $"{GetType().Name} {SessionId} {MessageId} {Payload}";
        }
    }
}
