using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class DeliveryStatusMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DeliveryStatus; } }

        public uint MessageId { get { return Payload.Peek32( 0 ); } set { Payload.Poke32( value, 0 ); } }
        public I2PDate Timestamp { get { return new I2PDate( new BufRefLen( Payload, 4 ) ); } set { value.Poke( Payload, 4 ); } }

        public DeliveryStatusMessage()
        {
            AllocateBuffer( 12 );
            var writer = new BufRefLen( Payload );
            Timestamp = new I2PDate( DateTime.UtcNow );
        }

        public DeliveryStatusMessage( uint msgid )
        {
            AllocateBuffer( 12 );
            var writer = new BufRefLen( Payload );
            Timestamp = new I2PDate( DateTime.UtcNow );
            MessageId = msgid;
        }

        public DeliveryStatusMessage( ulong networkid )
        {
            AllocateBuffer( 12 );
            var writer = new BufRefLen( Payload );
            writer.PokeFlip64( networkid, 4 );
            MessageId = BufUtils.RandomUint();
        }

        public DeliveryStatusMessage( I2NPHeader header, BufRef reader )
        {
            var start = new BufRef( reader );
            reader.Seek( 12 );
            SetBuffer( start, reader );
        }

        public bool IsNetworkId( ulong networkid )
        {
            return Payload.PeekFlip64( 4 ) == networkid;
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendFormat( "DeliveryStatus MessageId: {0}, Timestamp: {1}.", MessageId, Timestamp );

            return result.ToString();
        }
    }
}
