using System.Net.Sockets;
using Org.BouncyCastle.Crypto.Modes;
using I2PCore.Utils;
using System.Threading;

namespace I2PCore.Transport.NTCP
{
    public class NTCPReader
    {
        Socket MySocket;
        NTCPRunningContext Context;

        CbcBlockCipher Cipher;

        public NTCPReader( Socket s, NTCPRunningContext context )
        {
            MySocket = s;
            Context = context;
            Cipher = context.Dectryptor;
        }

        public BufLen Read()
        {
            if ( BlockLength == -1 ) return ReadLength();
            return ReadBlock();
        }

        int BlockLength = -1;
        public int NTCPDataSize = 0;

        byte[] InBuf = new byte[16384];
        int InBufPos = 0;
        int DecodeBufPos = 0;

        private BufLen ReadLength()
        {
            InBufPos = 0;
            DecodeBufPos = 0;

            while ( InBufPos < 16 )
            {
                if ( MySocket.Available > 0 )
                {
                    var len = MySocket.Receive( InBuf, InBufPos, 16 - InBufPos, SocketFlags.None );
                    if ( len == 0 ) throw new EndOfStreamEncounteredException();
                    InBufPos += len;
                }
                else
                {
                    Thread.Sleep( 400 );
                    if ( !MySocket.Connected ) throw new EndOfStreamEncounteredException();
                }
            }

            Cipher.ProcessBytes( new BufLen( InBuf, 0, 16 ) );
            BlockLength = BufUtils.Flip16( InBuf, 0 );
            NTCPDataSize = BlockLength;
            DecodeBufPos += 16;

            //Logging.LogTransport( string.Format( "NTCPReader block length: {0} bytes [0x{0:X}]", BlockLength ) );

            if ( BlockLength == 0 )
            {
                // Time Sync
                var result = new BufLen( InBuf, 0, 16 );
                BlockLength = -1;
                return result;
            }

            return ReadBlock();
        }

        private BufLen ReadBlock()
        {
            var inbufend = 2 + BlockLength + 4;
            inbufend += BufUtils.Get16BytePadding( inbufend );

            while ( InBufPos < inbufend )
            {
                if ( MySocket.Available > 0 )
                {
                    var len = MySocket.Receive( InBuf, InBufPos, inbufend - InBufPos, SocketFlags.None );
                    if ( len == 0 ) throw new EndOfStreamEncounteredException();
                    InBufPos += len;
                }
                else
                {
                    Thread.Sleep( 400 );
                    if ( !MySocket.Connected ) throw new EndOfStreamEncounteredException();
                }
            }

            Cipher.ProcessBytes( new BufLen( InBuf, 16, InBufPos - 16 ) );

            var checksum = LZUtils.Adler32( 1, new BufLen( InBuf, 0, InBufPos - 4 ) );
            var blocksum = BufUtils.Flip32( InBuf, InBufPos - 4 );

            if ( checksum != blocksum ) throw new ChecksumFailureException( "NTCPReader: Received Adler checksum mismatch." );

            var result = new BufLen( InBuf, 2, BlockLength );

            BlockLength = -1;

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( string.Format( "NTCPReader +{1}+ block received: {0} bytes [0x{0:X}]", result.Length, Context.TransportInstance ) );
#endif
            return result.Clone();
        }
    }
}
