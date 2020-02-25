using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Messages;

namespace I2PCore.TunnelLayer.I2NP.Data
{
    public class GarlicClove : I2PType
    {
        public GarlicCloveDelivery Delivery;
        public uint CloveId;
        public I2PDate Expiration;
        public I2NPMessage Message;

        public GarlicClove( BufRefLen reader )
        {
            Delivery = GarlicCloveDelivery.CreateGarlicCloveDelivery( reader );
            Message = I2NPMessage.ReadHeader16( reader ).Message;
            CloveId = reader.Read32();
            Expiration = new I2PDate( reader );
            reader.Seek( 3 ); // Cert
        }

        public GarlicClove( GarlicCloveDelivery delivery, I2PDate exp )
        {
            Delivery = delivery;
            Message = delivery.Message;
            CloveId = BufUtils.RandomUint();
            Expiration = exp;
        }

        public GarlicClove( GarlicCloveDelivery delivery )
        {
            Delivery = delivery;
            Message = delivery.Message;
            CloveId = BufUtils.RandomUint();
            Expiration = new I2PDate( DateTime.UtcNow + TimeSpan.FromSeconds( 8 ) );
        }

        static readonly byte[] ThreeZero = new byte[] { 0, 0, 0 };

        public void Write( BufRefStream dest )
        {
            Delivery.Write( dest );
            dest.Write( (BufRefLen)(BufLen)CloveId );
            Expiration.Write( dest );
            dest.Write( ThreeZero );
        }

        public override string ToString()
        {
            return $"{CloveId} {Delivery} {Message?.MessageType}";
        }
    }
}
