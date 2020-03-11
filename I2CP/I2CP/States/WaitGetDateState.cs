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

        internal override I2CPState MessageReceived( I2CPMessage msg )
        {
            if ( msg is GetDateMessage gdm )
            {
                var reply = new SetDateMessage( I2PDate.Now, gdm.Version ); // new I2PString( "0.1" ) ); // 
                Session.Send( reply );
                return new EstablishedState( Session );
            }
            return this;
        }
    }
}
