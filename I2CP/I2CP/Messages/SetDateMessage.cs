using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class SetDateMessage: I2CPMessage
    {
        public I2PDate Date;
        public I2PString Version;

        public SetDateMessage( I2PDate date, I2PString ver )
            : base( ProtocolMessageType.SetDate )
        {
            Date = date;
            Version = ver;
        }

        public SetDateMessage( BufRef data )
            : base( ProtocolMessageType.SetDate )
        {
            Date = new I2PDate( data );
            Version = new I2PString( data );
        }

        public override void Write( List<byte> dest )
        {
            WriteMessage( dest, Date, Version );
        }
    }
}
