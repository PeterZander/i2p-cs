using System;
using System.Collections.Generic;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.TunnelLayer
{
    public class ZeroHopTunnel: InboundTunnel
    {
        public override TickSpan Lifetime => TickSpan.Minutes( 5 );

        public override TickSpan TunnelEstablishmentTimeout => TickSpan.Seconds( 100 );

        public override bool NeedsRecreation => false;

        public ZeroHopTunnel( ITunnelOwner owner, TunnelConfig config, I2PIdentHash remotegateway )
            : base( owner, config, remotegateway )
        {
        }
    }
}
