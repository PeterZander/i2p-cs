using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class DestroySessionMessage : I2CPMessage
    {
        public ushort SessionId;

        public DestroySessionMessage( ushort sessid )
            : base( ProtocolMessageType.DestroySession )
        {
            SessionId = sessid;
        }

        public DestroySessionMessage( BufRef reader )
            : base( ProtocolMessageType.DestroySession )
        {
            SessionId = reader.ReadFlip16();
        }

        public override void Write( BufRefStream dest )
        {
            dest.Write( (BufRefLen)BufUtils.Flip16BL( SessionId ) );
        }
    }
}
