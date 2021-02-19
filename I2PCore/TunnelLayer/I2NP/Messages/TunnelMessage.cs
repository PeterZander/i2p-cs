using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using I2PCore.Data;
using I2PCore.TransportLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Utils;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public abstract class TunnelMessage
    {
        [Flags]
        public enum DeliveryTypes : byte { Local = 0x00 << 5, Tunnel = 0x01 << 5, Router = 0x02 << 5, Unused = 0x03 << 5 }

        public readonly DeliveryTypes Delivery;
        public readonly I2NPMessage Message;

        protected TunnelMessage( I2NPMessage message, DeliveryTypes dt ) 
        {
            Message = message; 
            Delivery = dt; 
        }

        public abstract void Distribute( Tunnel tunnel );

        public override string ToString()
        {
            return GetType().Name;
        }
    }

    public class TunnelMessageLocal: TunnelMessage
    {
        public TunnelMessageLocal( I2NPMessage message ) : base( message, DeliveryTypes.Local ) { }

        public override void Distribute( Tunnel tunnel )
        {
            tunnel.MessageReceived( Message, Message.CreateHeader16.HeaderAndPayload.Length );
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"{this}: Local dist to {tunnel}: {Message}" );
#endif
        }
    }

    public class TunnelMessageRouter : TunnelMessage
    {
        public readonly I2PIdentHash Destination;

        protected TunnelMessageRouter( I2NPMessage message, I2PIdentHash destination, DeliveryTypes dt )
            : base( message, dt )
        {
            Destination = destination;
        }

        public TunnelMessageRouter( I2NPMessage message, I2PIdentHash destination )
            : base( message, DeliveryTypes.Router ) 
        { 
            Destination = destination; 
        }

        public override void Distribute( Tunnel tunnel )
        {
            try
            {
                tunnel.Bandwidth.DataSent( Message.Payload.Length );
                TransportProvider.Send( Destination, Message );
            }
            catch( Exception ex )
            {
                Logging.LogWarning( $"{this}", ex );
                if ( ++tunnel.AggregateErrors > 5 ) throw;
            }
        }
    }

    public class TunnelMessageTunnel : TunnelMessageRouter
    {
        public readonly I2PTunnelId Tunnel;

        public TunnelMessageTunnel( I2NPMessage message, I2PIdentHash destination, I2PTunnelId tunnel )
            : base( message, destination, DeliveryTypes.Tunnel ) 
        { 
            Tunnel = tunnel; 
        }

        public TunnelMessageTunnel( I2NPMessage message, InboundTunnel tunnel )
            : base( message, tunnel.Destination, DeliveryTypes.Tunnel )
        {
            Tunnel = tunnel.GatewayTunnelId;
        }

        public override void Distribute( Tunnel tunnel )
        {
            try
            {
                var gwmsg = new TunnelGatewayMessage( Message, Tunnel );

                tunnel.Bandwidth.DataSent( gwmsg.Payload.Length );
                TransportProvider.Send( Destination, gwmsg );
            }
            catch ( Exception ex )
            {
                Logging.LogWarning( $"{this}", ex );
                if ( ++tunnel.AggregateErrors > 5 ) throw;
            }
        }
    }
}
