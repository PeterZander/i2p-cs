using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public abstract partial class I2NPMessage
    {
        public enum MessageTypes
        {
            DatabaseStore = 1,
            DatabaseLookup = 2,
            DatabaseSearchReply = 3,
            DeliveryStatus = 10,
            Garlic = 11,
            TunnelData = 18,
            TunnelGateway = 19,
            Data = 20,
            TunnelBuild = 21,
            TunnelBuildReply = 22,
            VariableTunnelBuild = 23,
            VariableTunnelBuildReply = 24
        }

        // Always allocated with space for a 16 byte header in front
        // Message payload starts at Payload.
        private BufLen Buf;

        public abstract MessageTypes MessageType { get; }

        // SetBuffer assumes that there is 16 bytes extra available in front of the message buffer.
        // If a message with a (received) 5 byte header is accessed with Header16 you will get an out 
        // of range exception, or faulty data.
        protected void SetBuffer( BufRef start, BufRef reader )
        {
            Buf = new BufLen( start, -I2NPHeader.I2NPMaxHeaderSize, ( reader - start ) + I2NPHeader.I2NPMaxHeaderSize );
        }

        protected void AllocateBuffer( int size )
        {
            Buf = new BufLen( new byte[size + I2NPHeader.I2NPMaxHeaderSize] );
        }

        protected BufLen Header5Buf { get { return new BufLen( Buf, I2NPHeader.I2NPMaxHeaderSize - 5, 5 ); } }
        protected BufLen Header5AndPayloadBuf { get { return new BufLen( Buf, I2NPHeader.I2NPMaxHeaderSize - 5 ); } }
        protected BufLen Header16Buf { get { return new BufLen( Buf, 0, I2NPHeader.I2NPMaxHeaderSize ); } }
        protected BufLen Header16AndPayloadBuf { get { return new BufLen( Buf, 0 ); } }

        public BufLen Payload { get { return new BufLen( Buf, I2NPHeader.I2NPMaxHeaderSize ); } }

        public static II2NPHeader16 ReadHeader16( BufRef reader )
        {
            return new I2NPHeader16( reader );
        }

        public static II2NPHeader5 ReadHeader5( BufRef reader )
        {
            return new I2NPHeader5( reader );
        }

        public II2NPHeader16 GetHeader16( uint messageid )
        {
            return new I2NPHeader16( this, messageid );
        }

        public II2NPHeader16 Header16
        {
            get
            {
                return new I2NPHeader16( this );
            }
        }

        public II2NPHeader5 Header5
        {
            get
            {
                return new I2NPHeader5( this );
            }
        }

#if DEBUG
        protected static void DebugCheckMessageCreation( I2NPMessage msg )
        {
            if ( msg.Header16AndPayloadBuf == null )
            {
                throw new NotImplementedException( "I2NPHeader16: '" + msg.GetType().Name + "' failed to set up a memory buffer for Header16!" );
            }

            if ( !object.ReferenceEquals( msg.Payload.BaseArray, msg.Header16AndPayloadBuf.BaseArray )
                || msg.Payload.BaseArrayOffset != 16 + msg.Header16AndPayloadBuf.BaseArrayOffset )
            {
                throw new NotImplementedException( "I2NPHeader16: '" + msg.GetType().Name + "' created wrong size of buffer for Header16!" );
            }
        }
#endif
    }
}
