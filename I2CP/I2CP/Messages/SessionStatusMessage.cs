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

        public enum SessionStates : byte { Destroyed = 0, Created = 1, Updated = 2, Invalid = 3, Refused = 4 }
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
            reader.Seek( 4 );
            if ( (ProtocolMessageType)reader.Read8() != MessageType ) throw new ArgumentException( "SessionStatusMessage( reader ) Wrong message type." );
            SessionId = reader.Read16();
            SessionState = (SessionStates)reader.Read8();
        }

        public override void Write( List<byte> dest )
        {
            dest.AddRange( BufUtils.Flip32B( 3 ) );
            dest.Add( (byte)MessageType );
            dest.AddRange( BitConverter.GetBytes( SessionId ) );
            dest.Add( (byte)SessionState );
        }
    }
}
