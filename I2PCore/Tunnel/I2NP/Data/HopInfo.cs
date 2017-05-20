using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class HopInfo
    {
        public readonly I2PKeysAndCert Peer;
        public readonly I2PTunnelId TunnelId;

        public I2PSessionKey IVKey;
        public I2PSessionKey LayerKey;

        public VariableTunnelBuildMessage.ReplyProcessingInfo ReplyProcessing;

        public HopInfo( I2PKeysAndCert dest, I2PTunnelId id )
        {
            Peer = dest;
            TunnelId = id;
        }
    }
}
