using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel
{
    public class TunnelConfig
    {
        public enum TunnelDirection { Outbound, Inbound };
        public enum TunnelPool { Initial, Client, Exploratory, External }

        public TunnelInfo Info;
        public TunnelDirection Direction;
        public TunnelPool Pool;

        public TunnelConfig( TunnelDirection dir, TunnelPool pool, TunnelInfo hops )
        {
            Direction = dir;
            Pool = pool;
            Info = hops;
        }
    }
}
