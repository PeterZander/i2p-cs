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
        public enum TunnelRole { Gateway, Participant, Endpoint }
        public enum TunnelPool { Initial, Client, Exploratory, External }

        public TunnelInfo Info;
        public TunnelDirection Direction;
        public TunnelRole Role;
        public TunnelPool Pool;

        public TunnelConfig( TunnelDirection dir, TunnelRole role, TunnelPool pool, TunnelInfo hops )
        {
            Direction = dir;
            Role = role;
            Pool = pool;
            Info = hops;
        }
    }
}
