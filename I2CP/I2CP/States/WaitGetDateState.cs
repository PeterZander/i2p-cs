using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2P.I2CP.Messages;
using I2PCore;
using I2PCore.Data;
using I2CP.I2CP.States;

namespace I2P.I2CP.States
{
    internal class WaitGetDateState: I2CPState
    {
        internal WaitGetDateState( I2CPSession sess ): base( sess ) { }

        internal override I2CPState Run()
        {
            if ( Timeout( HandshakeTimeout ) )
            {
                throw new FailedToConnectException( "I2CP WaitGetDateState " + Session.DebugId + " Failed to connect. Timeout." );
            }
            return this;
        }

        internal override I2CPState MessageReceived( I2CPMessage msg )
        {
            if ( msg.MessageType == I2CPMessage.ProtocolMessageType.GetDate )
            {
                var reply = new SetDateMessage( I2PDate.Now, new I2PString( "0.1" ) );
                Session.Send( reply );
                return new WaitForCreateSessionState( Session );
            }
            return this;
        }
    }
}
