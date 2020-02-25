using I2PCore.Utils;
using System.Net;
using I2PCore.Data;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;

namespace I2PCore.TransportLayer.SSU
{
    public abstract class SSUState
    {
        public static readonly TickSpan HandshakeStateTimeout = TickSpan.Milliseconds( 2000 );
        public const int HandshakeStateMaxRetries = 3;

        // UDPTransport.java
        // We used to have MAX_IDLE_TIME = 5m, but this causes us to drop peers
        // and lose the old introducer tags, causing introduction fails,
        // so we keep the max time long to give the introducer keepalive code
        // in the IntroductionManager a chance to work.
        //     public static final int EXPIRE_TIMEOUT = 20*60*1000;
        //     private static final int MAX_IDLE_TIME = EXPIRE_TIMEOUT;
        //     public static final int MIN_EXPIRE_TIMEOUT = 165*1000;

        // From PurpleI2P SSUSession.h SSU_TERMINATION_TIMEOUT
        //public const int InactivityTimeoutSeconds = 330;  
        public static TickSpan InactivityTimeout
        {
            get
            {
                return TickSpan.Seconds( 12 * 60 );
            }
        }

        public TickCounter Created = TickCounter.Now;
        public TickCounter LastSend = TickCounter.Now;
        public TickCounter LastReceive = TickCounter.Now;
        public int Retries = 0;

        protected SSUSession Session;

        protected SSUState( SSUSession sess ) { Session = sess; }

        protected bool Timeout( TickSpan timeout ) 
        { 
            return LastSend.DeltaToNow > timeout
                || LastReceive.DeltaToNow > timeout;
        }

        private void DataSent() { LastSend.SetNow(); }
        private void DataReceived() { LastReceive.SetNow(); }

        public abstract SSUState Run();

        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );
        protected abstract BufLen CurrentMACKey { get; }
        protected abstract BufLen CurrentPayloadKey { get; }

        protected enum MACHealth { Match, Missmatch, AbandonSession }

        public virtual SSUState DatagramReceived( BufRefLen recv, IPEndPoint RemoteEP )
        {
            // Verify the MAC
            var reader = new BufRefLen( recv );
            var header = new SSUHeader( reader );
            var recvencr = header.EncryptedBuf;

            var macstate = VerifyMAC( header, CurrentMACKey );

            switch ( macstate )
            {
                case MACHealth.AbandonSession:
                    Logging.LogTransport( $"SSU {this}: Abandoning session. MAC check failed." );

                    SendSessionDestroyed();
                    Session.Host.ReportEPProblem( RemoteEP );
                    return null;

                case MACHealth.Missmatch: 
                    return this;
            }

            // Decrypt
            Cipher.Init( false, CurrentPayloadKey.ToParametersWithIV( header.IV ) );
            Cipher.ProcessBytes( recvencr );

            header.SkipExtendedHeaders( reader );

            if ( header.MessageType == SSUHeader.MessageTypes.SessionDestroyed
                && CurrentPayloadKey != Session.RemoteIntroKey
                && CurrentPayloadKey != Session.MyRouterContext.IntroKey )
            {
                Logging.LogTransport( $"SSU {this}: Received SessionDestroyed." );
                SendSessionDestroyed();
                return null;
            }

            Logging.LogDebugData( $"SSU {this}: Received message: {header.MessageType}" +
                $": {SSUHost.SSUDateTime( header.TimeStamp )}" );

            DataReceived();

            return HandleMessage( header, reader );
        }

        protected void SendSessionDestroyed( 
            BufLen mackey = null,
            BufLen cryptokey = null )
        {
            if ( mackey is null ) mackey = CurrentMACKey;
            if ( cryptokey is null ) cryptokey = CurrentPayloadKey;

            Logging.LogTransport( $"SSU {this}: Sending SessionDestroyed." );

            SendMessage(
                SSUHeader.MessageTypes.SessionDestroyed,
                mackey,
                cryptokey,
                ( start, writer ) => true );

            Session.Host.EPStatisitcs.UpdateSessionLength( Session.RemoteEP, Session.StartTime.DeltaToNow );
        }

        // MAC verified and packet dectrypted
        public abstract SSUState HandleMessage( SSUHeader header, BufRefLen reader );

        readonly BufLen MACBuf = new BufLen( new byte[16] );

        private int ConsecutiveMACCheckFailures = 0;

        protected MACHealth VerifyMAC( SSUHeader header, BufLen key )
        {
            var macdata = new BufLen[] { 
                    header.MACDataBuf, 
                    header.IV, 
                    BufUtils.Flip16BL( (ushort)( (ushort)header.MACDataBuf.Length ^ I2PConstants.SSU_PROTOCOL_VERSION ) ) };

            var recvhash = I2PHMACMD5Digest.Generate( macdata, key, MACBuf );
            var ok = header.MAC.Equals( recvhash );
            var result = ok ? MACHealth.Match : MACHealth.Missmatch;

            if ( !ok )
            {
                if ( ++ConsecutiveMACCheckFailures > 3 )
                {
                    result = MACHealth.AbandonSession;
                }
            }
            else
            {
                ConsecutiveMACCheckFailures = 0;
            }

#if DEBUG
            if ( result != MACHealth.Match )
            {
                Logging.LogTransport( $"SSU {this}: VerifyMAC {result} [{ConsecutiveMACCheckFailures}] {key}" );
            }
            else
            {
                Logging.LogDebugData( $"SSU {this}: VerifyMAC {result} [{ConsecutiveMACCheckFailures}] {key}" );
            }
#endif

#if DEBUG && SSU_TRACK_OLD_MAC_KEYS
            if ( result != MACHealth.Match )
            {
                result = CheckOldKeys( header, macdata, MACBuf, result );
            }
#endif
            return result;
        }

#if SSU_TRACK_OLD_MAC_KEYS
        private MACHealth CheckOldKeys(
            SSUHeader header, 
            BufLen[] macdata, 
            BufLen mACBuf, 
            MACHealth result )
        {
            if ( !SSUSession.OldKeys.TryGetValue( Session.RemoteEP, out var oldkeys ) )
                return result;

            foreach ( var keyinfo in oldkeys.ToArray() )
            {
                var recvhashi = I2PHMACMD5Digest.Generate( macdata, keyinfo.Key, MACBuf );
                var oki = header.MAC.Equals( recvhashi );

                Logging.LogTransport( $"SSU {this}: " +
                    $"Old key from {keyinfo.Created} {keyinfo.CreatedDelta, -20} {keyinfo.Type, -10} match: {oki, -6} {keyinfo.Key}" );
            }

            return result;
        }
#endif

        protected delegate bool SendMessageGenerator( BufLen start, BufRefLen writer );
        protected CbcBlockCipher SendMessageCipher = new CbcBlockCipher( new AesEngine() );

        /**
         * From PacketBuilder.java
         -----8<-----
         * @param packet prepared packet with the first 32 bytes empty and a length
         *               whose size is mod 16.
         *               As of 0.9.7, length non-mod-16 is allowed; the
         *               last 1-15 bytes are included in the MAC calculation but are not encrypted.
         -----8<-----
         */

        protected void SendMessage( 
            IPEndPoint dest,
            SSUHeader.MessageTypes message, 
            BufLen mackey,
            BufLen cryptokey,
            SendMessageGenerator gen,
            SendMessageGenerator genextrapadding )
        {
            var start = Session.Host.SendBuffers.Pop();

            var writer = new BufRefLen( start );
            var header = new SSUHeader( writer, message );

            if ( !gen( start, writer ) ) return;

            // Do not cut to datalen & ~0xf as that might make data at the end unencrypted
            var datapadding = BufUtils.Get16BytePadding( writer - start );
            writer.Write( BufUtils.Random( datapadding ) ); 
            var datalen = writer - start;

            var encryptedbuf = new BufLen( start, 32, datalen - 32 );

            // TODO: Adding extra padding does not seem to work
            if ( genextrapadding != null ) if ( !genextrapadding( start, writer ) ) return;

            var packetlen = writer - start;
            var data = new BufLen( start, 0, packetlen );
            var hmac = new BufLen( data, 32 );

            SendMessageCipher.Init( true, cryptokey.ToParametersWithIV( header.IV ) );
            SendMessageCipher.ProcessBytes( encryptedbuf );

            I2PHMACMD5Digest.Generate( new BufLen[] { 
                        hmac, 
                        header.IV, 
                        BufUtils.Flip16BL( (ushort)( (ushort)hmac.Length ^ I2PConstants.SSU_PROTOCOL_VERSION ) )
                    }, mackey, header.MAC );

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( string.Format( "SSUState SendMessage {0}: encrlen {1} bytes [0x{1:X}] (padding {2} bytes [0x{2:X}]), " +
                "hmac {3} bytes [0x{3:X}], sendlen {4} bytes [0x{4:X}]",
                Session.DebugId,
                encryptedbuf.Length,
                datapadding,
                hmac.Length,
                data.Length ) );
#endif

            DataSent();
            Send( dest, data );
        }

        bool RandomExtraPadding( BufLen start, BufRefLen writer )
        {
            writer.Write( BufUtils.Random( BufUtils.RandomInt( 16 ) ) );
            return true;
        }

        protected void Send( IPEndPoint ep, BufLen data )
        {
            Session.Send( ep, data );
        }

        protected void SendMessage(
            SSUHeader.MessageTypes message,
            BufLen mackey,
            BufLen cryptokey,
            SendMessageGenerator gen,
            SendMessageGenerator genextrapadding )
        {
            SendMessage( Session.RemoteEP, message, mackey, cryptokey, gen, genextrapadding );
        }

        protected void SendMessage(
            SSUHeader.MessageTypes message,
            BufLen mackey,
            BufLen cryptokey,
            SendMessageGenerator gen )
        {
            SendMessage( Session.RemoteEP, message, mackey, cryptokey, gen, null );
        }

        protected void SendMessage(
            IPEndPoint dest,
            SSUHeader.MessageTypes message,
            BufLen mackey,
            BufLen cryptokey,
            SendMessageGenerator gen )
        {
            SendMessage( dest, message, mackey, cryptokey, gen, null );
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Session.DebugId}";
        }
    }
}
