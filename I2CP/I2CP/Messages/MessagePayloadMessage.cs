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
        I2PMessagePayload Payload;

        public MessagePayloadMessage( byte[] payload, ref int ix )
            : base( ProtocolMessageType.MessagePayload )
        {
            Payload = new I2PMessagePayload();
            Payload.SessionId = BitConverter.ToUInt16( payload, ix );
            Payload.MessageId = BitConverter.ToUInt32( payload, ix + 2 );
            ix += 6;

            var len = BitConverter.ToUInt32( payload, ix );
            ix += 4;

            var buf = new byte[len];
            Array.Copy( buf, 0, payload, ix, len );
            Payload.Payload = buf;
        }

        public override void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
