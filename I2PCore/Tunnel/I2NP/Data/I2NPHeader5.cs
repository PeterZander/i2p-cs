using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Transport.SSU;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public interface II2NPHeader5 : II2NPHeader
    {
    }

    public partial class I2NPMessage
    {
        protected class I2NPHeader5 : I2NPHeader, II2NPHeader5
        {
            const int HeaderLength = 5;

            public I2NPHeader5( BufRef reader )
                : base( reader )
            {
                MessageRef = I2NPUtil.GetMessage( this, reader );
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public I2NPHeader5( I2NPMessage msg ): base( msg.Header5Buf )
            {
                MessageRef = msg;

                MessageType = msg.MessageType;
                Expiration = I2PDate.DefaultI2NPExpiration();
#if DEBUG
                DebugCheckMessageCreation( MessageRef );
#endif
            }

            public override I2PDate Expiration
            {
                get
                {
                    return new I2PDate( SSUHost.SSUDateTime( Buf.PeekFlip32( 1 ) ) );
                }
                set
                {
                    Buf.PokeFlip32( SSUHost.SSUTime( (DateTime)value ), 1 );
                }
            }

            public override BufLen HeaderAndPayload { get { return MessageRef.Header5AndPayloadBuf; } }
            public override int Length { get { return HeaderLength; } }

            public static I2NPMessage GetMessage( BufRef reader )
            {
                var hdr = new I2NPHeader5( reader );
                return hdr.MessageRef;
            }

            public static BufLen ToBufLen( I2NPMessage msg )
            {
                var hdr = new I2NPHeader5( msg );
                return new BufLen( hdr.ToByteArray() );
            }

            public override void Write( List<byte> dest )
            {
                MessageRef.Header5AndPayloadBuf.WriteTo( dest );
            }
        }
    }
}
