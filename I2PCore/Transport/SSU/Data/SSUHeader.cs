using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class SSUHeader
    {
        public const int REKEY_DATA_LENGTH = 64;
        public const int UNENCRYPTED_HEADER_SIZE = 32;
        public const int FIXED_HEADER_SIZE = UNENCRYPTED_HEADER_SIZE + 5;

        [Flags]
        public enum MessageFlags : byte
        {
            ExtendedOptionsFlag = 0x04,
            RekeyFlag = 0x08,
            PayloadType = 0xf0
        }

        public enum MessageTypes : byte
        {
            SessionRequest = 0 << 4,
            SessionCreated = 1 << 4,
            SessionConfirmed = 2 << 4,
            RelayRequest = 3 << 4,
            RelayResponse = 4 << 4,
            RelayIntro = 5 << 4,
            Data = 6 << 4,
            PeerTest = 7 << 4,
            SessionDestroyed = 8 << 4
        }

        public readonly BufLen MAC;
        public readonly BufLen IV;

        public readonly BufLen FlagBuf;
        public MessageFlags Flag 
        {
            get
            {
                return (MessageFlags)( FlagBuf[0] & ~(byte)MessageFlags.PayloadType );
            }
            set
            {
                FlagBuf[0] = (byte)( ( FlagBuf[0] & ~(byte)MessageFlags.PayloadType ) | (byte)value );
            }
        }

        public MessageTypes MessageType 
        { 
            get 
            {
                return (MessageTypes)( FlagBuf[0] & (byte)MessageFlags.PayloadType ); 
            } 
            set 
            {
                FlagBuf[0] = (byte)( ( FlagBuf[0] & ~(byte)MessageFlags.PayloadType ) | (byte)value ); 
            } 
        }

        public readonly BufLen TimeStampBuf;
        public uint TimeStamp { get { return TimeStampBuf.PeekFlip32( 0 ); } set { TimeStampBuf.PokeFlip32( value, 0 ); } }
        public DateTime TimeStampDayTime { get { return SSUHost.SSUDateTime( TimeStamp ); } }

        public readonly BufLen MACDataBuf;
        public readonly BufLen EncryptedBuf;
        public readonly BufLen PostTimestampBuf;

        public BufLen RekeyData
        {
            get
            {
                if ( ( Flag & MessageFlags.RekeyFlag ) != MessageFlags.RekeyFlag ) return null;
                return new BufLen( PostTimestampBuf, 0, REKEY_DATA_LENGTH );
            }
        }

        public BufLen ExtendedOptions
        {
            get
            {
                if ( ( Flag & MessageFlags.ExtendedOptionsFlag ) != MessageFlags.ExtendedOptionsFlag ) return null;

                var rd = RekeyData;
                var offset = rd != null ? rd.Length : 0;

                var len = PostTimestampBuf.Peek8( offset );
                return new BufLen( PostTimestampBuf, offset + 1, len );
            }
        }

        public SSUHeader( BufRefLen reader )
        {
            MAC = reader.ReadBufLen( 16 );
            IV = reader.ReadBufLen( 16 );
            MACDataBuf = new BufLen( reader );
            EncryptedBuf = new BufLen( MACDataBuf, 0, MACDataBuf.Length & 0x7ffffff0 );
            FlagBuf = reader.ReadBufLen( 1 );
            TimeStampBuf = reader.ReadBufLen( 4 );
            PostTimestampBuf = new BufLen( reader );
        }

        public SSUHeader( BufRef writer, MessageTypes msgtype )
        {
            MAC = writer.ReadBufLen( 16 );
            IV = writer.ReadBufLen( 16 );
            IV.Randomize();
            FlagBuf = writer.ReadBufLen( 1 );
            MessageType = msgtype;
            TimeStampBuf = writer.ReadBufLen( 4 );
            TimeStamp = SSUHost.SSUTime( DateTime.UtcNow );
        }

        public void SkipExtendedHeaders( BufRefLen reader )
        {
            if ( (byte)( Flag & MessageFlags.RekeyFlag ) != 0 )
            {
#if NO_LOG_ALL_TRANSPORT
                Logging.Log( "SSUHeader: Rekey data skipped." );
#endif
                reader.Seek( REKEY_DATA_LENGTH );
            }

            //DebugUtils.LogDebug( "SSUHeader flags: " + ( FlagBuf[0] & 0x0f ).ToString() );

            if ( (byte)( Flag & MessageFlags.ExtendedOptionsFlag ) != 0 )
            {
#if NO_LOG_ALL_TRANSPORT
                Logging.Log( "SSUHeader: Extended options data skipped. " + ExtendedOptions.Length.ToString() + " bytes." );
#endif
                reader.Seek( ExtendedOptions.Length + 1 );
            }
        }
    }
}
