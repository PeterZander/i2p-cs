using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto;
using I2PCore.Data;
using System.IO;

namespace I2P.I2CP.Messages
{
    public class CreateSessionMessage: I2CPMessage
    {
        I2PSessionConfig Config;

        public CreateSessionMessage( I2PSessionConfig cfg ): base( ProtocolMessageType.CreateSession )
        {
            Config = cfg;
        }

        public override void Write( List<byte> dest )
        {
            Config.Write( dest );
        }
    }
}
