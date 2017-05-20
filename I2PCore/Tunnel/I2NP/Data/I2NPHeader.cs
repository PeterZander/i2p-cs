using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.Tunnel.I2NP.Data
{
    public interface II2NPHeader
    {
        I2NPMessage.MessageTypes MessageType { get; set; }
        I2PDate Expiration { get; set; }
        BufLen HeaderAndPayload { get; }
        int Length { get; }

        I2NPMessage Message { get; }

        string ToString();
    }

    public abstract class I2NPHeader: I2PType, II2NPHeader
    {
        public const int I2NPMaxHeaderSize = 16;
        public readonly I2PTunnelId FromTunnel = 0xFFFFFFFF;

        protected BufLen Buf;

        public I2NPMessage.MessageTypes MessageType
        {
            get { return (I2NPMessage.MessageTypes)Buf.Peek8( 0 ); }
            set { Buf.Poke8( (byte)value, 0 ); }
        }

        public abstract I2PDate Expiration { get; set; }
        public abstract int Length { get; }

        public abstract BufLen HeaderAndPayload { get; }
        protected I2NPMessage MessageRef;
        public I2NPMessage Message { get { return MessageRef; } }

        protected I2NPHeader( BufLen buf ) 
        { 
            Buf = buf; 
        }

        protected I2NPHeader( BufRef reader )
        {
            Buf = reader.ReadBufLen( Length );
        }

        protected I2NPHeader( BufRef reader, I2PTunnelId tunnel )
        {
            Buf = reader.ReadBufLen( Length );
            FromTunnel = tunnel;
        }

        static ItemFilterWindow<uint> RecentMessageIds = new ItemFilterWindow<uint>( TickSpan.Minutes( 10 ), 1 );
        public static uint GenerateMessageId()
        {
            int iter = 0;
            uint result;

            lock ( RecentMessageIds )
            {
                do
                {
                    result = BufUtils.RandomUint();
                } while ( result == 0 || ( RecentMessageIds.Count( result ) > 0 && ++iter < 100 ) );

                RecentMessageIds.Update( result );
            }
            return result;
        }

        public abstract void Write( List<byte> dest );

        public override string ToString()
        {
            return this.GetType().Name + " " + MessageType.ToString();
        }
    }
}
