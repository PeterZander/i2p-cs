using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class MessageStatusMessage: I2CPMessage
    {
        public ushort SessionId;
        public uint MessageId;
        public uint AvailableMessageSize;
        public uint ClientNonce;

        public enum MessageStatatuses : byte 
        {
            Available = 0,
            Accepted = 1,
            BestEffortSuccess = 2,
            BestEffortFailure = 3,
            GuaranteedSuccess = 4,
            GuaranteedFailure = 5,
            LocalSuccess = 6,
            LocalFailure = 7,
            RouterFailure = 8,
            NetworkFailure = 9,
            BadSession = 10,
            BadMessage = 11,
            BadOptions = 12,
            OverflowFailure = 13,
            MessageExpired = 14,
            BadLocalLeaseset = 15,
            NoLocalTunnels = 16,
            UnsupportedEncryption = 17,
            BadDestination = 18,
            BadLeaseset = 19,
            ExpiredLeaseset = 20,
            NoLeaseset = 21,
        }
        public MessageStatatuses MessageStatus;

        public MessageStatusMessage(
                ushort sessionid,
                uint messageid,
                MessageStatatuses messagestatus,
                uint availablemessagesize,
                uint clientnonce
            ) : base( ProtocolMessageType.MessageStatus )
        {
            SessionId = sessionid;
            MessageId = messageid;
            MessageStatus = messagestatus;
            AvailableMessageSize = availablemessagesize;
            ClientNonce = clientnonce;
        }

    public MessageStatusMessage( BufRef reader )
            : base( ProtocolMessageType.MessageStatus )
        {
            throw new NotImplementedException();
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[15];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.WriteFlip32( MessageId );
            writer.Write8( (byte)MessageStatus );
            writer.WriteFlip32( AvailableMessageSize );
            writer.WriteFlip32( ClientNonce );
            dest.Write( header );
        }
    }
}
