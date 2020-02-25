using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
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

#if DEBUG
        protected enum HeaderStates { Invalid, Header16, Header5 }

        private HeaderStates HeaderStateField = HeaderStates.Invalid;
        protected HeaderStates HeaderState
        {
            get => HeaderStateField;
            set
            {
                if ( HeaderStateField == value ) return;
                HeaderStateField = value;
                HeaderStateChanged?.Invoke();
            }
        }

        internal event Action HeaderStateChanged;
        internal void PayloadChanged() => HeaderStateChanged?.Invoke();
#endif

        // Always allocated with space for a 16 byte header in front
        // Message payload starts at Payload.
        private BufLen Buf;

        public abstract MessageTypes MessageType { get; }

        uint? MessageIdField;
        public virtual uint MessageId
        {
            get
            {
                if ( MessageIdField.HasValue )
                {
                    return MessageIdField.Value;
                }

                MessageIdField = I2NPMessage.GenerateMessageId();
                return MessageIdField.Value;
            }
            set
            {
                MessageIdField = value;
#if DEBUG
                HeaderState = HeaderStates.Invalid;
#endif
            }
        }

        private I2PDate ExpirationField = null;
        public I2PDate Expiration
        {
            get
            {
                if ( ExpirationField != null )
                {
                    return ExpirationField;
                }

                ExpirationField = I2PDate.DefaultI2NPExpiration();
                return ExpirationField;
            }
            set
            {
                ExpirationField = value;
#if DEBUG
                HeaderState = HeaderStates.Invalid;
#endif
            }
        }

        // SetBuffer assumes that there is 16 bytes extra available in front of the message buffer.
        // If a message with a (received) 5 byte header is accessed with Header16 you will get an out 
        // of range exception, or faulty data.
        protected void SetBuffer( BufRef start, BufRef reader )
        {
            Buf = new BufLen( start, -I2NPHeader16.I2NPMaxHeaderSize, ( reader - start ) + I2NPHeader16.I2NPMaxHeaderSize );
#if DEBUG
            HeaderState = HeaderStates.Invalid;
#endif
        }

        protected void AllocateBuffer( int size )
        {
            Buf = new BufLen( new byte[size + I2NPHeader16.I2NPMaxHeaderSize] );
#if DEBUG
            HeaderState = HeaderStates.Invalid;
#endif
        }

        protected BufLen Header5Buf 
        { 
            get 
            {
#if DEBUG
                HeaderState = HeaderStates.Header5;
#endif
                return new BufLen( Buf, I2NPHeader16.I2NPMaxHeaderSize - 5 ); 
            } 
        }

        protected BufLen Header16Buf 
        { 
            get 
            {
#if DEBUG
                HeaderState = HeaderStates.Header16;
#endif
                return new BufLen( Buf ); 
            } 
        }

        public BufLen Payload { get { return new BufLen( Buf, I2NPHeader16.I2NPMaxHeaderSize ); } }

        public static II2NPHeader16 ReadHeader16( BufRefLen reader )
        {
            return new I2NPHeader16( reader );
        }

        public II2NPHeader16 CreateHeader16
        {
            get
            {
                return new I2NPHeader16( this );
            }
        }

#if DEBUG
        protected static void DebugCheckMessageCreation( I2NPMessage msg )
        {
            if ( msg.Buf is null )
            {
                throw new NotImplementedException( $"I2NPMessage: '{msg.GetType().Name}' " +
                    $"failed to set up a memory buffer" );
            }
        }
#endif

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

    }
}
