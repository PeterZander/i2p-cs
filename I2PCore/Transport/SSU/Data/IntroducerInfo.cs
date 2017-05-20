using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.Transport.SSU.Data
{
    internal class IntroducerInfo
    {
        internal IPAddress Host;
        internal int Port;
        internal BufLen IntroKey;
        internal uint IntroTag;

        internal IPEndPoint EndPoint { get { return new IPEndPoint( Host, Port ); } }

        internal IntroducerInfo( string host, string port, string ikey, string tag )
        {
            Host = IPAddress.Parse( host );
            Port = int.Parse( port );
            IntroKey = new BufLen( FreenetBase64.Decode( ikey ) );
            IntroTag = uint.Parse( tag );
        }
    }
}
