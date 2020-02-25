using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.SessionLayer;

namespace I2PCore.TunnelLayer.I2NP.Data
{
    public class TunnelInfo
    {
        public readonly List<HopInfo> Hops;

        public TunnelInfo( List<HopInfo> hops )
        {
            Hops = hops;
        }
    }
}
