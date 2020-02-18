﻿using System;
using System.Collections.Generic;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.Tunnel
{
    public class ZeroHopTunnel: InboundTunnel
    {
        public override TickSpan Lifetime => TickSpan.Seconds( 20 );

        public override TickSpan TunnelEstablishmentTimeout => TickSpan.Seconds( 100 );

        public ZeroHopTunnel( ITunnelOwner owner, TunnelConfig config )
            : base( owner, config )
        {
        }
    }
}
