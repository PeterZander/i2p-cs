using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public interface II2NPHeader16 : II2NPHeader
    {
        uint MessageId { get; set; }
        ushort PayloadLength { get; set; }
        byte PayloadChecksum { get; set; }
    }

    public partial class I2NPMessage
    {
        protected sealed class I2NPHeader16 : II2NPHeader16
        {
            public const int I2NPMaxHeaderSize = 16;
            const int HeaderLength = 16;

            private BufLen Buf;

            public I2NPMessage.MessageTypes MessageType
            {
                get { return (I2NPMessage.MessageTypes)Buf.Peek8( 0 ); }
                set { Buf.Poke8( (byte)value, 0 ); }
            }

            public BufLen HeaderAndPayload
            {
                get
                {
                    if ( !HeaderStateOk )
                    {
                        throw new InvalidOperationException(
                            $"{this}: Message state have changed" );
                    }
                    return Buf;
                }
            }

            /// <summary>
            /// Not flipped as it is just an id.
            /// </summary>
            public uint MessageId
            {
                get { return Buf.Peek32( 1 ); }
                set { Buf.Poke32( value, 1 ); }
            }

            public I2PDate Expiration
            {
                get { return new I2PDate( Buf.PeekFlip64( 5 ) ); }
                set { Buf.PokeFlip64( (ulong)value, 5 ); }
            }

            public int Length { get { return HeaderLength; } }

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

            private I2NPMessage MessageRefField;
            private I2NPMessage MessageRef
            {
                get => MessageRefField;
                set
                {
                    MessageRefField = value;
#if DEBUG
                    MessageRefField.HeaderStateChanged += HeaderStateInvalid;
#endif
                }
            }

            private bool HeaderStateOk = true;

#if DEBUG
            private void HeaderStateInvalid()
            {
                HeaderStateOk = false;
            }
#endif

            public I2NPMessage Message { get { return MessageRef; } }

            // Created from stream
            public I2NPHeader16( BufRefLen reader )
            {
                Buf = new BufLen( reader );

                reader.Seek( HeaderLength );

                MessageRef = I2NPUtil.GetMessage( 
                        MessageType, 
                        reader, 
                        MessageId );

                MessageRef.Expiration = Expiration;
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            // Created from I2PMessage
            public I2NPHeader16( I2NPMessage msg )
            {
                Buf = msg.Buf;

                MessageRef = msg;

                MessageType = msg.MessageType;
                Expiration = msg.Expiration;
                MessageId = msg.MessageId;

                PayloadLength = (ushort)msg.Payload.Length;

                var s = I2PHashSHA256.GetHash( msg.Payload );
                PayloadChecksum = s[0];
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public void Write( BufRefStream dest )
            {
                HeaderAndPayload.WriteTo( dest );
            }

            public override string ToString()
            {
                return $"{GetType().Name} MessageId: {MessageId}";
            }
        }
    }
}
