using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2P.I2CP.Messages;
using I2PCore;

namespace I2P.I2CP.States
{
    internal class WaitProtVer: I2CPState
    {
        public const byte I2CPProtocolVersion = 0x2a;

        internal WaitProtVer( I2CPSession sess ) : base( sess ) { }

        internal override I2CPState Run()
        {
            if ( Timeout( HandshakeTimeout ) )
            {
                throw new FailedToConnectException( "I2CP WaitProtVer " + Session.DebugId + " Failed to connect. Timeout." );
            }
            return this;
        }

        internal override I2CPState MessageReceived( I2CPMessage msg )
        {
            var ns = new WaitGetDateState( Session );
            ns.MessageReceived( msg );
            return ns;
        }

        internal override I2CPState DataReceived( BufLen recv )
        {
            if ( recv.Length == 0 ) return this;
            var recvreader = new BufRefLen( recv );
            byte b = recvreader.Read8();

            while ( b != I2CPProtocolVersion && recv.Length > 0 ) b = recvreader.Read8();

            if ( b == I2CPProtocolVersion ) return base.DataReceived( (BufLen)recvreader );
            return this;
        }
    }
}
