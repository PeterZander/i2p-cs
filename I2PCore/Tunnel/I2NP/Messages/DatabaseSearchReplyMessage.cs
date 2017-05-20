using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class DatabaseSearchReplyMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DatabaseSearchReply; } }

        public readonly I2PIdentHash Key;
        public readonly List<I2PIdentHash> Peers = new List<I2PIdentHash>();
        public readonly I2PIdentHash From;

        public DatabaseSearchReplyMessage( BufRef reader )
        {
            var start = new BufRef( reader );

            Key = new I2PIdentHash( reader );

            var peercount = reader.Read8();
            for ( int i = 0; i < peercount; ++i )
            {
                Peers.Add( new I2PIdentHash( reader ) );
            }

            From = new I2PIdentHash( reader );

            SetBuffer( start, reader );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "DatabaseSearchReplyMessage" );
            result.AppendLine( "Peer count   : " + ( Peers == null ? "(null)" : Peers.Count.ToString() ) );

            foreach ( var one in Peers )
            {
                result.AppendLine( one.ToString() );
            }

            return result.ToString();
        }
    }
}
