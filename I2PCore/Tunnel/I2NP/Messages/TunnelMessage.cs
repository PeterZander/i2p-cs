using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public abstract class TunnelMessage
    {
        [Flags]
        public enum DeliveryTypes : byte { Local = 0x00 << 5, Tunnel = 0x01 << 5, Router = 0x02 << 5, Unused = 0x03 << 5 }

        public readonly DeliveryTypes Delivery;
        public readonly II2NPHeader16 Header;

        protected TunnelMessage( II2NPHeader16 header, DeliveryTypes dt ) { Header = header; Delivery = dt; }
    }

    public class TunnelMessageLocal: TunnelMessage
    {
        public TunnelMessageLocal( II2NPHeader16 header ) : base( header, DeliveryTypes.Local ) { }
    }

    public class TunnelMessageRouter : TunnelMessage
    {
        public readonly I2PIdentHash Destination;

        protected TunnelMessageRouter( II2NPHeader16 header, I2PIdentHash destination, DeliveryTypes dt )
            : base( header, dt )
        {
            Destination = destination;
        }

        public TunnelMessageRouter( II2NPHeader16 header, I2PIdentHash destination )
            : base( header, DeliveryTypes.Router ) 
        { 
            Destination = destination; 
        }
    }

    public class TunnelMessageTunnel : TunnelMessageRouter
    {
        public readonly I2PTunnelId Tunnel;

        public TunnelMessageTunnel( II2NPHeader16 header, I2PIdentHash destination, I2PTunnelId tunnel )
            : base( header, destination, DeliveryTypes.Tunnel ) 
        { 
            Tunnel = tunnel; 
        }

        public TunnelMessageTunnel( II2NPHeader16 header, InboundTunnel tunnel )
            : base( header, tunnel.Destination, DeliveryTypes.Tunnel )
        {
            Tunnel = tunnel.GatewayTunnelId;
        }
    }
}
