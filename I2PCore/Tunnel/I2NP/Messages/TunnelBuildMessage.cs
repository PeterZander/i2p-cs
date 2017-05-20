using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class TunnelBuildMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.TunnelBuild; } }

        public List<AesEGBuildRequestRecord> Records = new List<AesEGBuildRequestRecord>();

        public TunnelBuildMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            for ( int i = 0; i < 8; ++i )
            {
                var r = new AesEGBuildRequestRecord( reader );
                Records.Add( r );
            }
            SetBuffer( start, reader );
        }   

        private TunnelBuildMessage()
        {
            AllocateBuffer( 1 + 8 * AesEGBuildRequestRecord.Length );
            var writer = new BufRefLen( Payload );
            for ( int i = 0; i < 8; ++i ) Records.Add( new AesEGBuildRequestRecord( writer ) );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "TunnelBuild" );
            for ( int i = 0; i < Records.Count; ++i )
            {
                result.Append( Records[i].ToString() );
            }

            return result.ToString();
        }
    }
}
