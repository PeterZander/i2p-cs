using System;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    public class SSUHeader
    {
        public const int REKEY_DATA_LENGTH = 64;
        public const int UNENCRYPTED_HEADER_SIZE = 32;
        public const int FIXED_HEADER_SIZE = UNENCRYPTED_HEADER_SIZE + 5;

        [Flags]
        public enum MessageFlags : byte
        {
            ExtendedOptionsFlag     = 0b0000_0100,
            RekeyFlag               = 0b0000_1000,
            PayloadType             = 0b1111_0000
        }

        public enum MessageTypes : byte
        {
            SessionRequest      = 0 << 4,
            SessionCreated      = 1 << 4,
            SessionConfirmed    = 2 << 4,
            RelayRequest        = 3 << 4,
            RelayResponse       = 4 << 4,
            RelayIntro          = 5 << 4,
            Data                = 6 << 4,
            PeerTest            = 7 << 4,
            SessionDestroyed    = 8 << 4
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
                FlagBuf[0] = (byte)( ( FlagBuf[0] & (byte)MessageFlags.PayloadType ) 
                    | ( (byte)value & ~(byte)MessageFlags.PayloadType ) );
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
        public readonly BufLen ExtendedOptionsBuf;
        public uint TimeStamp { get { return TimeStampBuf.PeekFlip32( 0 ); } set { TimeStampBuf.PokeFlip32( value, 0 ); } }
        public DateTime TimeStampDayTime { get { return SSUHost.SSUDateTime( TimeStamp ); } }

        public readonly BufLen MACDataBuf;
        public readonly BufLen EncryptedBuf;
        public readonly BufLen PostTimeStampBuf;

        public readonly BufLen RekeyData;

        public BufLen ExtendedOptions;

        public SSUHeader( BufRefLen reader )
        {
            MAC = reader.ReadBufLen( 16 );
            IV = reader.ReadBufLen( 16 );
            MACDataBuf = new BufLen( reader );
            EncryptedBuf = new BufLen( MACDataBuf, 0, MACDataBuf.Length & 0x7fff_fff0 );
            FlagBuf = reader.ReadBufLen( 1 );
            TimeStampBuf = reader.ReadBufLen( 4 );
            PostTimeStampBuf = new BufLen( reader );
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

        // Call after decrypting
        public void SkipExtendedHeaders( BufRefLen reader )
        {
            if ( Flag.HasFlag( MessageFlags.RekeyFlag ) )
            {
                // Currently not implemented by anyone
                //RekeyData = reader.ReadBufLen( REKEY_DATA_LENGTH );
            }

            if ( Flag.HasFlag( MessageFlags.ExtendedOptionsFlag ) )
            {
                var len = reader.Read8();
                ExtendedOptions = reader.ReadBufLen( len );
            }
        }

        public override string ToString()
        {
            return $"SSUHeader Flagbuf: {FlagBuf}, MessageType: {MessageType}, TimeStamp: {SSUHost.SSUDateTime( TimeStamp )}";
        }
    }
}
