using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;
using I2PCore.Data;
using I2PCore.Router;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace I2PCore.Transport.SSU
{
    public abstract class SSUState
    {
        public const int HandshakeStateTimeoutSeconds = 10;
        public const int HandshakeStateMaxRetries = 3;

        // UDPTransport.java
        // We used to have MAX_IDLE_TIME = 5m, but this causes us to drop peers
        // and lose the old introducer tags, causing introduction fails,
        // so we keep the max time long to give the introducer keepalive code
        // in the IntroductionManager a chance to work.
        //     public static final int EXPIRE_TIMEOUT = 20*60*1000;
        //     private static final int MAX_IDLE_TIME = EXPIRE_TIMEOUT;
        //     public static final int MIN_EXPIRE_TIMEOUT = 165*1000;

        //public const int InactivityTimeoutSeconds = 330;  // From PurpleI2P SSUSession.h SSU_TERMINATION_TIMEOUT
        public const int InactivityTimeoutSeconds = 12 * 60; // Nearly all CPU is used for DH negotiations.

        public TickCounter Created = TickCounter.Now;
        public TickCounter LastAction = TickCounter.Now;
        public int Retries = 0;

        protected SSUSession Session;

        protected SSUState( SSUSession sess ) { Session = sess; }

        protected bool Timeout( int seconds ) { return LastAction.DeltaToNowSeconds > seconds || Created.DeltaToNow.ToMinutes > 20; }
        protected void DataSent() { LastAction.SetNow(); }

        public abstract SSUState Run();

        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );
        protected abstract BufLen CurrentMACKey { get; }
        protected abstract BufLen CurrentPayloadKey { get; }

        protected enum MACHealth { Match, Missmatch, UseOurIntroKey, AbandonSession }

        public virtual SSUState DatagramReceived( BufRefLen recv, IPEndPoint RemoteEP )
        {
            // Verify the MAC
            var reader = new BufRefLen( recv );
            var header = new SSUHeader( reader );
            var recvencr = header.EncryptedBuf;

            var macstate = VerifyMAC( header, CurrentMACKey );

            var usekey = CurrentPayloadKey;

            switch ( macstate )
            {
                case MACHealth.AbandonSession: return null;
                case MACHealth.Missmatch: return this;
                case MACHealth.UseOurIntroKey:
                    usekey = Session.MyRouterContext.IntroKey;
                    break;
            }

            // Decrypt
            Cipher.Init( false, usekey.ToParametersWithIV( header.IV ) );
            Cipher.ProcessBytes( recvencr );

            header.SkipExtendedHeaders( reader );

            return HandleMessage( header, reader );
        }

        // MAC verified and packet dectrypted
        public abstract SSUState HandleMessage( SSUHeader header, BufRefLen reader );

        int IntroMACsReceived = 0;

        BufLen MACBuf = new BufLen( new byte[16] );

        protected MACHealth VerifyMAC( SSUHeader header, BufLen key )
        {
            var macdata = new BufLen[] { 
                    header.MACDataBuf, 
                    header.IV, 
                    BufUtils.Flip16BL( (ushort)( (ushort)header.MACDataBuf.Length ^ I2PConstants.SSU_PROTOCOL_VERSION ) ) };
            var recvhash = I2PHMACMD5Digest.Generate( macdata, key, MACBuf );
            var ok = header.MAC.Equals( recvhash );
            if ( ok )
            {
                IntroMACsReceived = 0;
            }
            else
            {
                Logging.LogDebug( () => string.Format( "SSU {0}: {1} Current MAC check fail. Payload {2} bytes. ",
                    this, Session.DebugId, header.MACDataBuf.Length ) );

                if ( (int)Logging.LogLevel <= (int)Logging.LogLevels.Debug )
                {
                    if ( Session.IntroKey != null )
                    {
                        var recvhashi = I2PHMACMD5Digest.Generate( macdata, new BufLen( Session.IntroKey ), MACBuf );
                        var oki = header.MAC.Equals( recvhashi );
                        Logging.Log( "SSU " + this.ToString() + ": " + Session.DebugId + ". " +
                            "Session Intro key match: " + oki.ToString() );
                    }
                }

                var recvhash2 = I2PHMACMD5Digest.Generate( macdata, new BufLen( Session.MyRouterContext.IntroKey ), MACBuf );
                var ok2 = header.MAC.Equals( recvhash2 );
                Logging.LogDebug( () => string.Format( "SSU {0}: {1} My intro MAC key match: {3}. Payload {2} bytes. ",
                    this, Session.DebugId, header.MACDataBuf.Length, ok2 ) );

                if ( ok2 )
                {
                    if ( ++IntroMACsReceived > 5 )
                    {
                        var reason = string.Format( "SSU {0}: {1}. {2} intro key matches in a row. The other side seems to have started a new session.",
                            this, Session.DebugId, IntroMACsReceived );

                        Logging.Log( reason );
                        return MACHealth.AbandonSession;
                    }

                    return MACHealth.UseOurIntroKey;
                }
            }

            return ok ? MACHealth.Match : MACHealth.Missmatch;
        }

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

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( string.Format( "SSUState SendMessage {0}: encrlen {1} bytes [0x{1:X}] (padding {2} bytes [0x{2:X}]), " +
                "hmac {3} bytes [0x{3:X}], sendlen {4} bytes [0x{4:X}]",
                Session.DebugId,
                encryptedbuf.Length,
                datapadding,
                hmac.Length,
                data.Length ) );
#endif

            DataSent();
            Session.Host.Send( dest, data );
        }

        bool RandomExtraPadding( BufLen start, BufRefLen writer )
        {
            writer.Write( BufUtils.Random( BufUtils.RandomInt( 16 ) ) );
            return true;
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
    }
}
