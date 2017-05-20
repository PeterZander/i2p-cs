using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2P.I2CP.Messages;
using I2PCore;
using I2PCore.Data;
using I2P.I2CP.States;
using I2P.I2CP;

namespace I2CP.I2CP.States
{
    internal class WaitForEstablishedDestinationState: I2CPState
    {
        internal WaitForEstablishedDestinationState( I2CPSession sess ) : base( sess ) { }

        internal override I2CPState Run()
        {
            if ( Timeout( EstablishedDestinationTimeout ) )
            {
                throw new FailedToConnectException( "I2CP WaitForEstablishedDestinationState " + Session.DebugId + " Failed to connect. Timeout." );
            }
            return this;
        }

        internal override I2CPState MessageReceived( I2CPMessage msg )
        {
            return this;
        }
    }
}
