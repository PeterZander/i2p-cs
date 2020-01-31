using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class RequestVariableLeaseSetMessage: I2CPMessage
    {
        public List<I2PLease> Leases = new List<I2PLease>();

        public RequestVariableLeaseSetMessage( BufRef reader )
            : base( ProtocolMessageType.RequestVarLS )
        {
            var leases = reader.ReadFlip16();
            for ( int i = 0; i < leases; ++i )
            {
                Leases.Add( new I2PLease( reader ) );
            }
        }

        public override void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
