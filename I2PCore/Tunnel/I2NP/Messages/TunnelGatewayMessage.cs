using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class TunnelGatewayMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.TunnelGateway; } }

        public TunnelGatewayMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            reader.Seek( 6 + reader.PeekFlip16( 4 ) );
            SetBuffer( start, reader );
        }

        public TunnelGatewayMessage( II2NPHeader16 header, I2PTunnelId outtunnel )
        {
            var msg = header.HeaderAndPayload;
            AllocateBuffer( 6 + msg.Length );

            TunnelId = outtunnel;
            GatewayMessageLength = (ushort)msg.Length;
            // TODO: Removey mem copy
            Payload.Poke( msg, 6 );
        }

        public uint TunnelId
        {
            get
            {
                return Payload.Peek32( 0 );
            }
            set
            {
                Payload.Poke32( value, 0 );
            }
        }

        public ushort GatewayMessageLength
        {
            get
            {
                return Payload.PeekFlip16( 4 );
            }
            set
            {
                Payload.PokeFlip16( value, 4 );
            }
        }

        public BufLen GatewayMessage
        {
            get
            {
                return new BufLen( Payload, 6, GatewayMessageLength );
            }
        }

        protected MessageTypes GatewayMessageType
        {
            get
            {
                return (I2NPMessage.MessageTypes)GatewayMessage.Peek8( 0 );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "TunnelGateway" );
            result.AppendLine( "TunnelId:          : " + TunnelId.ToString() );
            result.AppendLine( "GatewayMessageType : " + GatewayMessageType.ToString() + " ( " + GatewayMessageLength.ToString() + " bytes )" );

            return result.ToString();
        }

    }
}
