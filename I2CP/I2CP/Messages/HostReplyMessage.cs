using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class HostReplyMessage : I2CPMessage
    {
        public enum HostLookupResults: byte
        { 
            Success = 0,
            Failure = 1,
            LookupPasswordRequired = 2,
            PrivateKeyRequired = 3,
            LookupPasswordAndPrivateKeyRequired = 4,
            LeasesetDecryptionFailure = 5,
        }

        public ushort SessionId;
        public uint RequestId;
        public HostLookupResults ResultCode;
        public I2PDestination Destination;

        public HostReplyMessage( ushort sessid, uint reqid, HostLookupResults rescode )
            : base( ProtocolMessageType.HostLookupReply )
        {
            SessionId = sessid;
            RequestId = reqid;
            ResultCode = rescode;
        }

        public HostReplyMessage( ushort sessid, uint reqid, I2PDestination dest )
            : base( ProtocolMessageType.HostLookupReply )
        {
            SessionId = sessid;
            RequestId = reqid;
            ResultCode = HostLookupResults.Success;
            Destination = dest;
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[7];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.WriteFlip32( RequestId );
            writer.Write8( (byte)ResultCode );
            dest.Write( header );

            if ( ResultCode == HostLookupResults.Success )
            {
                Destination.Write( dest );
            }
        }

        public override string ToString()
        {
            return $"{GetType().Name} {SessionId} {RequestId} {ResultCode} {Destination}";
        }
    }
}
