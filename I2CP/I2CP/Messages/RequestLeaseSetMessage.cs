using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class RequestLeaseSetMessage: I2CPMessage
    {
        public ushort SessionId;
        public List<I2PLease> Leases = new List<I2PLease>();

        public RequestLeaseSetMessage( ushort sessionid, IEnumerable<I2PLease> leases )
            : base( ProtocolMessageType.RequestLS )
        {
            SessionId = sessionid;
            Leases.AddRange( leases );
        }

        public RequestLeaseSetMessage( BufRef reader )
            : base( ProtocolMessageType.RequestLS )
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
            var buf = new byte[3];
            var writer = new BufRefLen( buf );
            writer.WriteFlip16( SessionId );
            writer.Write8( (byte)Leases.Count );
            dest.Write( buf );

            for ( int i = 0; i < Leases.Count; ++i )
            {
                Leases[i].TunnelGw.Write( dest );
                Leases[i].TunnelId.Write( dest );
            }
            new I2PDate( DateTime.UtcNow.AddMinutes( 9 ) ).Write( dest );
        }
    }
}
