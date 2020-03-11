using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class SendMessageMessage : I2CPMessage
    {
        public ushort SessionId;
        public I2PDestination Destination;
        public BufLen Payload;
        public uint Nonce;

        public SendMessageMessage( BufRefLen reader )
            : base( ProtocolMessageType.SendMessage )
        {
            SessionId = reader.ReadFlip16();
            Destination = new I2PDestination( reader );
            var len = reader.ReadFlip32();
            Payload = reader.ReadBufLen( (int)len );
            Nonce = reader.ReadFlip32();
        }

        public override void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
