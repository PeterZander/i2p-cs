using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public interface II2NPHeader16 : II2NPHeader
    {
        uint MessageId { get; set; }
        ushort PayloadLength { get; set; }
        byte PayloadChecksum { get; set; }
    }

    public partial class I2NPMessage
    {
        protected class I2NPHeader16 : I2NPHeader, II2NPHeader16
        {
            const int HeaderLength = 16;

            /// <summary>
            /// Not flipped as it is just an id.
            /// </summary>
            public uint MessageId
            {
                get { return Buf.Peek32( 1 ); }
                set { Buf.Poke32( value, 1 ); }
            }

            public override I2PDate Expiration
            {
                get { return new I2PDate( Buf.PeekFlip64( 5 ) ); }
                set { Buf.PokeFlip64( (ulong)value, 5 ); }
            }

            public override BufLen HeaderAndPayload { get { return MessageRef.Header16AndPayloadBuf; } }
            public override int Length { get { return HeaderLength; } }

            public ushort PayloadLength
            {
                get { return Buf.PeekFlip16( 13 ); }
                set { Buf.PokeFlip16( value, 13 ); }
            }

            public byte PayloadChecksum
            {
                get { return Buf.Peek8( 15 ); }
                set { Buf.Poke8( value, 15 ); }
            }

            public I2NPHeader16( BufRef reader )
                : base( reader )
            {
                MessageRef = I2NPUtil.GetMessage( this, reader );
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public I2NPHeader16( BufRef reader, I2PTunnelId fromtunnel )
                : base( reader, fromtunnel )
            {
                MessageRef = I2NPUtil.GetMessage( this, reader );
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public I2NPHeader16( I2NPMessage msg )
                : this( msg, GenerateMessageId() )
            {
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public I2NPHeader16( I2NPMessage msg, uint messageid ): base( msg.Header16Buf )
            {
                MessageRef = msg;

                MessageType = msg.MessageType;
                Expiration = I2PDate.DefaultI2NPExpiration();
                MessageId = messageid;

                PayloadLength = (ushort)msg.Payload.Length;

                var s = I2PHashSHA256.GetHash( msg.Payload );
                PayloadChecksum = s[0];
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public static void Write( BufRefStream dest, I2NPMessage msg )
            {
                var inst = new I2NPHeader16( msg );
                inst.Write( dest );
            }

            public override void Write( BufRefStream dest )
            {
                MessageRef.Header16AndPayloadBuf.WriteTo( dest );
            }

            public static I2NPMessage GetMessage( BufRef reader )
            {
                var hdr = new I2NPHeader16( reader );
                return hdr.MessageRef;
            }

            public static BufLen ToBufLen( I2NPMessage msg )
            {
                var hdr = new I2NPHeader16( msg );
                return new BufLen( hdr.ToByteArray() );
            }

            public override string ToString()
            {
                return base.ToString() + " MessageId: " + MessageId.ToString();
            }
        }
    }
}
