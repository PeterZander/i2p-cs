using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2P.I2CP.Messages
{
    public abstract class I2CPMessage
    {
        public enum PayloadFormat : byte
        {
            Streaming = 6,
            Datagram = 17, 
            Raw = 18,
        };

        public enum ProtocolMessageType : byte
        {
            CreateSession = 1,
            ReconfigSession = 2,
            DestroySession = 3,
            CreateLS = 4,
            SendMessage = 5,
            RecvMessageBegin = 6,
            RecvMessageEnd = 7,
            GetBWLimits = 8,
            SessionStatus = 20,
            RequestLS = 21,
            MessageStatus = 22,
            BWLimits = 23,
            ReportAbuse = 29,
            Disconnect = 30,
            MessagePayload = 31,
            GetDate = 32,
            SetDate = 33,
            DestLookup = 34,
            DestReply = 35,
            SendMessageExpires = 36,
            RequestVarLS = 37,
            HostLookup = 38,
            HostLookupReply = 39,
            CreateLeaseSet2Message_Deprecated = 40,
            CreateLeaseSet2Message = 41,
        }

        public readonly ProtocolMessageType MessageType;

        protected I2CPMessage( ProtocolMessageType msgtype )
        {
            MessageType = msgtype;
        }

        public abstract void Write( BufRefStream dest );

        /*
        public void WriteMessage( BufRefStream dest, params I2PType[] fields )
        {
            var buf = new BufRefStream();
            foreach ( var field in fields ) field.Write( buf );

            dest.Write( BufUtils.Flip32B( (uint)buf.Length ) );
            dest.Write( (byte)MessageType );
            dest.Write( buf );
        }
        */

        public byte[] ToByteArray()
        {
            var buf = new BufRefStream();
            Write( buf );
            return buf.ToArray();
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
