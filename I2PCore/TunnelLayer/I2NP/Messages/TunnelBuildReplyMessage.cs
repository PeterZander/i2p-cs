using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Utils;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class TunnelBuildReplyMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.TunnelBuildReply; } }

        public BuildResponseRecord[] ResponseRecords;

        public TunnelBuildReplyMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            ResponseRecords = new BuildResponseRecord[8];

            for ( int i = 0; i < 8; ++i ) ResponseRecords[i] = new BuildResponseRecord( reader );
            SetBuffer( start, reader );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "TunnelBuildReply" );

            if ( ResponseRecords == null )
            {
                result.AppendLine( "Content: (null)" );
            }
            else
            {
                for ( int i = 0; i < 8; ++i )
                {
                    result.AppendLine( "ResponseRecords[" + i.ToString() + "]" );
                    result.AppendLine( ResponseRecords[i].ToString() );
                }
            }

            return result.ToString();
        }
    }
}
