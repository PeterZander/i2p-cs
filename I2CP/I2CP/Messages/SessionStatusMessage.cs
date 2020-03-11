using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class SessionStatusMessage: I2CPMessage
    {
        public ushort SessionId;

        public enum SessionStates : byte { Destroyed = 0, Created = 1, Updated = 2, Invalid = 3, Refused = 4, NoLeaseSet = 21 }
        public SessionStates SessionState;

        public SessionStatusMessage( ushort sessid, SessionStates state )
            : base( ProtocolMessageType.SessionStatus )
        {
            SessionId = sessid;
            SessionState = state;
        }

        public SessionStatusMessage( BufRef reader )
            : base( ProtocolMessageType.SessionStatus )
        {
            SessionId = reader.ReadFlip16();
            SessionState = (SessionStates)reader.Read8();
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[3];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.Write8( (byte)SessionState );
            dest.Write( header );
        }
    }
}
