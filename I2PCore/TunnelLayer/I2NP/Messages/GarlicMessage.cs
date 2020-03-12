using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class GarlicMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.Garlic; } }

        public BufLen EGData
        {
            get
            {
                return new BufLen( Payload, 4 );
            }
        }

        public GarlicMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            var len = (int)reader.PeekFlip32( 0 );
            reader.Seek( len + 4 );
            SetBuffer( start, reader );
        }
    }
}
