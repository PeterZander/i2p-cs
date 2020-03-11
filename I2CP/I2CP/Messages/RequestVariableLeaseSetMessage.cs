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
        public ushort SessionId;
        public List<I2PLease> Leases = new List<I2PLease>();

        public RequestVariableLeaseSetMessage( ushort sessionid, IEnumerable<I2PLease> leases )
            : base( ProtocolMessageType.RequestVarLS )
        {
            SessionId = sessionid;
            Leases.AddRange( leases );
        }

        public RequestVariableLeaseSetMessage( BufRef reader )
            : base( ProtocolMessageType.RequestVarLS )
        {
            SessionId = reader.ReadFlip16();
            var leases = reader.Read8();
            for ( int i = 0; i < leases; ++i )
            {
                Leases.Add( new I2PLease( reader ) );
            }
        }

        public override void Write( BufRefStream dest )
        {
            var header = new byte[3];
            var writer = new BufRefLen( header );
            writer.WriteFlip16( SessionId );
            writer.Write8( (byte)Leases.Count );
            dest.Write( header );

            for ( int i = 0; i < Leases.Count; ++i )
            {
                Leases[i].Write( dest );
            }
        }
    }
}
