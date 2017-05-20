using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.Tunnel.I2NP.Data
{
    public abstract class GarlicCloveDelivery: I2PType
    {
        public enum DeliveryMethod : byte { Local = 0x00 << 5, Destination = 0x01 << 5, Router = 0x02 << 5, Tunnel = 0x03 << 5 }

        [Flags]
        public enum DeliveryFlags : byte { Encrypted = 0x80, Delay = 0x10 }

        public I2NPMessage Message;
        public byte Flag;
        public DeliveryMethod Delivery;

        public I2PSessionKey SessionKey; // Not used in current implementations
        public uint Delay; // Not used in current implementations

        //protected GarlicCloveDelivery() { } 

        public GarlicCloveDelivery( DeliveryMethod mtd )
        {
            Flag = (byte)mtd;
            Delivery = mtd;
        }

        public GarlicCloveDelivery( I2NPMessage msg, DeliveryMethod mtd )
        {
            Message = msg;
            Flag = (byte)mtd;
            Delivery = mtd;
        }

        public static GarlicCloveDelivery CreateGarlicCloveDelivery( BufRef reader )
        {
            var flag = reader.Read8();
            var deliv = (DeliveryMethod)( flag & ( 0x03 << 5 ) );

            switch ( deliv )
            {
                case DeliveryMethod.Local:
                    return new GarlicCloveDeliveryLocal( reader, flag );

                case DeliveryMethod.Destination:
                    return new GarlicCloveDeliveryDestination( reader, flag );

                case DeliveryMethod.Router:
                    return new GarlicCloveDeliveryRouter( reader, flag );

                case DeliveryMethod.Tunnel:
                    return new GarlicCloveDeliveryTunnel( reader, flag );

                default:
                    throw new NotImplementedException( "Unknown delivery method in Garlic clove!" );
            }
        }

        public virtual void Write( List<byte> dest )
        {
            byte flag = Flag;
            if ( SessionKey != null ) flag |= (byte)DeliveryFlags.Encrypted;
            if ( Delay != 0 ) flag |= (byte)DeliveryFlags.Delay;
            dest.Add( flag );

            if ( SessionKey != null ) SessionKey.Write( dest );
        }
    }

    public class GarlicCloveDeliveryLocal : GarlicCloveDelivery
    {
        public GarlicCloveDeliveryLocal( I2NPMessage msg ) : base( msg, DeliveryMethod.Local ) { }

        public GarlicCloveDeliveryLocal( BufRef reader, byte flag ): base( DeliveryMethod.Local )
        {
            Flag = flag;
            if ( ( Flag & (byte)DeliveryFlags.Encrypted ) != 0 ) SessionKey = new I2PSessionKey( reader );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) Delay = reader.ReadFlip32();
        }

        public override void Write( List<byte> dest )
        {
            base.Write( dest );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) dest.AddRange( BufUtils.Flip32B( 0 ) );
            dest.AddRange( Message.Header16.HeaderAndPayload );
        }
    }

    public class GarlicCloveDeliveryDestination : GarlicCloveDelivery
    {
        public readonly I2PIdentHash Destination;
        public GarlicCloveDeliveryDestination( I2NPMessage msg, I2PIdentHash dest ) : base( msg, DeliveryMethod.Destination ) 
        {
            Destination = dest;
        }

        public GarlicCloveDeliveryDestination( BufRef reader, byte flag ): base( DeliveryMethod.Destination )
        {
            Flag = flag;
            if ( ( Flag & (byte)DeliveryFlags.Encrypted ) != 0 ) SessionKey = new I2PSessionKey( reader );
            Destination = new I2PIdentHash( reader );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) Delay = reader.ReadFlip32();
        }

        public override void Write( List<byte> dest )
        {
            base.Write( dest );
            Destination.Write( dest );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) dest.AddRange( BufUtils.Flip32B( 0 ) );
            dest.AddRange( Message.Header16.HeaderAndPayload );
        }
    }

    public class GarlicCloveDeliveryRouter : GarlicCloveDelivery
    {
        public readonly I2PIdentHash Destination;
        public GarlicCloveDeliveryRouter( I2NPMessage msg, I2PIdentHash dest ) : base( msg, DeliveryMethod.Router )
        {
            Destination = dest;
        }

        public GarlicCloveDeliveryRouter( BufRef reader, byte flag ): base( DeliveryMethod.Router )
        {
            Flag = flag;
            if ( ( Flag & (byte)DeliveryFlags.Encrypted ) != 0 ) SessionKey = new I2PSessionKey( reader );
            Destination = new I2PIdentHash( reader );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) Delay = reader.ReadFlip32();
        }

        public override void Write( List<byte> dest )
        {
            base.Write( dest );
            Destination.Write( dest );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) dest.AddRange( BufUtils.Flip32B( 0 ) );
            dest.AddRange( Message.Header16.HeaderAndPayload );
        }
    }

    public class GarlicCloveDeliveryTunnel : GarlicCloveDelivery
    {
        public readonly I2PIdentHash Destination;
        public readonly I2PTunnelId Tunnel;
        public GarlicCloveDeliveryTunnel( I2NPMessage msg, I2PIdentHash dest, I2PTunnelId tunnel ) 
            : base( msg, DeliveryMethod.Tunnel )
        {
            Destination = dest;
            Tunnel = tunnel;
        }

        public GarlicCloveDeliveryTunnel( I2NPMessage msg, InboundTunnel tunnel )
            : base( msg, DeliveryMethod.Tunnel )
        {
            Destination = tunnel.Destination;
            Tunnel = tunnel.GatewayTunnelId;
        }

        public GarlicCloveDeliveryTunnel( BufRef reader, byte flag ): base( DeliveryMethod.Tunnel )
        {
            Flag = flag;
            if ( ( Flag & (byte)DeliveryFlags.Encrypted ) != 0 ) SessionKey = new I2PSessionKey( reader );
            Destination = new I2PIdentHash( reader );
            Tunnel = new I2PTunnelId( reader );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) Delay = reader.ReadFlip32();
        }

        public override void Write( List<byte> dest )
        {
            base.Write( dest );
            Destination.Write( dest );
            Tunnel.Write( dest );
            if ( ( Flag & (byte)DeliveryFlags.Delay ) != 0 ) dest.AddRange( BufUtils.Flip32B( 0 ) );
            dest.AddRange( Message.Header16.HeaderAndPayload );
        }
    }
}
