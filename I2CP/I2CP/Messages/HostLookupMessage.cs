using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class HostLookupMessage : I2CPMessage
    {
        public enum HostLookupTypes: byte { Hash = 0, HostName = 1 }

        public ushort SessionId;
        public uint RequestId;
        public uint TimeoutMilliseconds;
        public HostLookupTypes RequestType;
        public I2PIdentHash Hash;
        public I2PString HostName;

        public HostLookupMessage( BufRefLen reader )
            : base( ProtocolMessageType.HostLookup )
        {
            SessionId = reader.ReadFlip16();
            RequestId = reader.ReadFlip32();
            TimeoutMilliseconds = reader.ReadFlip32();
            RequestType = (HostLookupTypes)reader.Read8();

            switch ( RequestType )
            {
                case HostLookupTypes.Hash:
                    Hash = new I2PIdentHash( reader );
                    break;

                case HostLookupTypes.HostName:
                    HostName = new I2PString( reader );
                    break;
            }
        }

        public override void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return $"{GetType().Name} {SessionId} {RequestId} {Hash?.Id32Short} {HostName}";
        }
    }
}
