using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class GarlicClove : I2PType
    {
        public GarlicCloveDelivery Delivery;
        public uint CloveId;
        public I2PDate Expiration;
        public I2NPMessage Message;

        public GarlicClove( BufRef reader )
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
            CloveId = BufUtils.RandomUint();
            Expiration = exp;
        }

        public void Write( List<byte> dest )
        {
            Delivery.Write( dest );
            dest.AddRange( (BufLen)CloveId );
            Expiration.Write( dest );
            dest.Add( 0 );
            dest.Add( 0 );
            dest.Add( 0 );
        }
    }
}
