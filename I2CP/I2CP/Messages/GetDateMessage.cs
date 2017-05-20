using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class GetDateMessage: I2CPMessage
    {
        I2PString Version;
        I2PMapping Mapping;

        public GetDateMessage( string ver, I2PMapping map )
            : base( ProtocolMessageType.GetDate )
        {
            Version = new I2PString( ver );
            Mapping = map == null ? new I2PMapping(): map;
        }

        public GetDateMessage( BufRef reader )
            : base( ProtocolMessageType.GetDate )
        {
            reader.Seek( 4 );
            if ( (ProtocolMessageType)reader.Read8() != MessageType ) throw new ArgumentException( "GetDateMessage( reader ) Wrong message type." );
            Version = new I2PString( reader );
            Mapping = new I2PMapping( reader );
        }

        public override void Write( List<byte> dest )
        {
            Version.Write( dest );
            Mapping.Write( dest );
        }
    }
}
