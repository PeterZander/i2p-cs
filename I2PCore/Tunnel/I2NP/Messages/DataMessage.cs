using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class DataMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.Data; } }

        public DataMessage( I2NPHeader header, BufRef reader )
        {
            var start = new BufRef( reader );
            reader.Seek( (int)reader.ReadFlip32() );
            SetBuffer( start, reader );
        }

        public DataMessage( BufLen data )
        {
            AllocateBuffer( 4 + data.Length );
            Payload.PokeFlip32( (uint)data.Length, 0 );
            Payload.Poke( data, 4 );
        }

        public uint DataMessagePayloadLength
        {
            get
            {
                return Payload.PeekFlip32( 0 );
            }
            set
            {
                Payload.PokeFlip32( value, 0 );
            }
        }

        public BufLen DataMessagePayload
        {
            get
            {
                return new BufLen( Payload, 4, (int)DataMessagePayloadLength );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "DataMessage bytes: " + Payload.Length );

            return result.ToString();
        }
    }
}
