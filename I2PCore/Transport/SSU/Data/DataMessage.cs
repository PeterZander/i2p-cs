using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class SSUDataMessage
    {
        public List<uint> ExplicitAcks;
        public List<KeyValuePair<uint,List<byte>>> AckBitfields;
        public bool ECN;
        public BufLen ExtData;

        [Flags]
        public enum DataMessageFlags : byte
        {
            ExplicitAcks = 0x80,
            BitfieldAcks = 0x40,
            ECN = 0x10,
            RequestPreviousAcks = 0x08,
            WantReply = 0x04,
            ExtendedDataIncluded = 0x02
        }

        public List<RebuildI2NPMessage> NewMessages;

        public SSUDataMessage( BufRef reader, DataDefragmenter fragments )
        {
            var dataflags = (DataMessageFlags)reader.Read8();
#if LOG_ALL_TRANSPORT
            Logging.LogTransport( "SSU DataMessage rececived flag: " + dataflags.ToString() );
#endif
            var explicitacks = ( dataflags & DataMessageFlags.ExplicitAcks ) != 0;
            var acksbitfields = ( dataflags & DataMessageFlags.BitfieldAcks ) != 0;
            ECN = ( dataflags & DataMessageFlags.ECN ) != 0;
            var extdata = ( dataflags & DataMessageFlags.ExtendedDataIncluded ) != 0;
            if ( explicitacks )
            {
                ExplicitAcks = new List<uint>();
                var acks = reader.Read8();
                for ( int i = 0; i < acks; ++i ) ExplicitAcks.Add( reader.Read32() );
            }
            if ( acksbitfields )
            {
                var bitfields = reader.Read8();
                AckBitfields = new List<KeyValuePair<uint, List<byte>>>( bitfields );
                for ( int i = 0; i < bitfields; ++i )
                {
                    var msgid = reader.Read32();
                    var bfs = new List<byte>( 10 );
                    byte bf;
                    while ( true )
                    {
                        bf = reader.Read8();
                        bfs.Add( (byte)( bf & 0x7f ) );
                        if ( ( bf & 0x80 ) == 0 ) break;
                    }
                    AckBitfields.Add( new KeyValuePair<uint, List<byte>>( msgid, bfs ) );
                }
            }
            if ( extdata )
            {
                var datasize = reader.Read8();
                ExtData = reader.ReadBufLen( datasize );
            }

            var fragcount = reader.Read8();
            for ( int i = 0; i < fragcount; ++i )
            {
                var frag = new DataFragment( reader );
                var newmessage = fragments.Add( frag );

                if ( newmessage != null )
                {
                    if ( NewMessages == null ) NewMessages = new List<RebuildI2NPMessage>();
                    NewMessages.Add( newmessage );
                }
            }
        }
    }
}
