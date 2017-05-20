using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Tunnel
{
    internal class TunnelSettings
    {
        internal float OutboundTunnelBitrateLimit = 0f;
        internal float InboundTunnelBitrateLimit = 0f;

        /// <summary>
        /// 0.0f (share nothing) -> 1.0f (only share)
        /// </summary>
        internal float BandwidthShareRatio = 0.5f;

        internal const float EndpointTunnelBitrateLimit = 500f * 1024f;
        internal const float GatewayTunnelBitrateLimit = 500f * 1024f;
        internal const float PassthroughTunnelBitrateLimit = 500f * 1024f;
    }
}
