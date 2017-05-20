using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class VariableTunnelBuildReplyMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.VariableTunnelBuildReply; } }

        public List<BuildResponseRecord> ResponseRecords;

        public VariableTunnelBuildReplyMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            ResponseRecords = new List<BuildResponseRecord>();

            byte count = reader.Read8();
            for ( int i = 0; i < count; ++i ) ResponseRecords.Add( new BuildResponseRecord( reader ) );
            SetBuffer( start, reader );
        }

        public VariableTunnelBuildReplyMessage( IEnumerable<BuildResponseRecord> recs )
        {
            AllocateBuffer( 1 + recs.Count() * EGBuildRequestRecord.Length );
            ResponseRecords = new List<BuildResponseRecord>( recs );

            // TODO: Remove mem copy
            var writer = new BufRefLen( Payload );
            writer.Write8( (byte)recs.Count() );
            foreach ( var rec in ResponseRecords ) writer.Write( rec.Payload );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "VariableTunnelBuildReply" );
            if ( ResponseRecords != null )
            {
                foreach ( var one in ResponseRecords )
                {
                    result.AppendLine( one.ToString() );
                }
            }

            return result.ToString();
        }
    }
}
