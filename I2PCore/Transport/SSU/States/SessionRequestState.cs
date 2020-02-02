using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;
using I2PCore.Router;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Data;
using I2PCore.Transport.SSU.Data;

namespace I2PCore.Transport.SSU
{
    public class SessionRequestState: SSUState
    {
        internal I2PPrivateKey PrivateKey;
        internal I2PPublicKey X;
        internal I2PPublicKey Y;

        internal SessionCreated SCMessage;

        public SessionRequestState( SSUSession sess ): base( sess )
        {
            var keys = I2PPrivateKey.GetNewKeyPair();
            PrivateKey = keys.PrivateKey;
            X = keys.PublicKey;
        }

        protected override BufLen CurrentMACKey { get { return Session.IntroKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.IntroKey; } }

        PeriodicAction ResendSessionRequestAction = new PeriodicAction( TickSpan.Seconds( HandshakeStateTimeoutSeconds / 5 ), true );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeoutSeconds ) )
            {
                throw new FailedToConnectException( "SSU SessionRequestState " + Session.DebugId + " Failed to connect. Timeout." );
            }

            ResendSessionRequestAction.Do( () =>
            {
                if ( ++Retries > HandshakeStateMaxRetries )
                    throw new FailedToConnectException( "SSU SessionRequestState " + Session.DebugId + " Failed to connect. Too many retries." );

                SendSessionRequest();
            } );

            return this;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            var tstime = SSUHost.SSUDateTime( header.TimeStamp );

            if ( header.MessageType != SSUHeader.MessageTypes.SessionCreated )
            {
#if LOG_ALL_TRANSPORT
                Logging.LogTransport( "SSU SessionRequestState: Received unexpected message " + tstime.ToString() + " : " + header.Flag.ToString() );
#endif
                return this;
            }

            SCMessage = new SessionCreated( reader, Session.RemoteRouter.Certificate );

            Session.RelayTag = SCMessage.RelayTag;

            Y = new I2PPublicKey( (BufRefLen)SCMessage.Y, Session.RemoteRouter.Certificate );
            BufUtils.DHI2PToSessionAndMAC( out Session.SharedKey, out Session.MACKey,
                Y.ToBigInteger().ModPow( PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

            var ipaddr = new IPAddress( SCMessage.Address.ToByteArray() );
            ushort port = SCMessage.Port.PeekFlip16( 0 );
            Session.SignOnTimeB = SCMessage.SignOnTime.Peek32( 0 );
            var btime = SSUHost.SSUDateTime( BufUtils.Flip32( Session.SignOnTimeB ) );

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( "SSU SessionRequestState " + Session.DebugId + " : Received SessionCreated. " + tstime.ToString() + " : " + btime.ToString() );
#endif
            Session.Host.ReportedAddress( ipaddr );

            if ( !I2PSignature.SupportedSignatureType( Session.RemoteRouter.Certificate.SignatureType ) )
                throw new SignatureCheckFailureException( "SSU SessionRequestState " + Session.DebugId + " : " +
                    "Received non supported signature type: " +
                    Session.RemoteRouter.Certificate.SignatureType.ToString() );

            var cipher = new CbcBlockCipher( new AesEngine() );
            cipher.Init( false, Session.SharedKey.ToParametersWithIV( header.IV ) );
            cipher.ProcessBytes( SCMessage.SignatureEncrBuf );

            var baddr = new BufLen( Session.RemoteEP.Address.GetAddressBytes() );
            var sign = new I2PSignature( (BufRefLen)SCMessage.Signature, Session.RemoteRouter.Certificate );

            var sok = I2PSignature.DoVerify(
                Session.RemoteRouter.SigningPublicKey, sign,
                X.Key, Y.Key, 
                SCMessage.Address, SCMessage.Port,
                baddr, BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port ), 
                SCMessage.RelayTag, SCMessage.SignOnTime );

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( "SSU SessionRequestState: Signature check: " + sok.ToString() + ". " + Session.RemoteRouter.Certificate.SignatureType.ToString() );
#endif
            if ( !sok )
            {
                throw new SignatureCheckFailureException( "SSU SessionRequestState " + Session.DebugId + ": Received SessionCreated signature check failed." +
                    Session.RemoteRouter.Certificate.ToString() );
            }

            var relaytag = SCMessage.RelayTag.PeekFlip32( 0 );
            if ( relaytag != 0 )
            {
                Session.Host.IntroductionRelayOffered( 
                    new IntroducerInfo( 
                        Session.RemoteEP.Address, 
                        (ushort)Session.RemoteEP.Port, 
                        Session.IntroKey, relaytag ) );
            }

            Logging.LogTransport( "SSU SessionRequestState: Session " + Session.DebugId + " created. Moving to SessionConfirmedState." );
            Session.ReportConnectionEstablished();
            return new SessionConfirmedState( Session, this );
        }

        private void SendSessionRequest()
        {
            Logging.LogTransport( "SSU SessionRequestState " + Session.DebugId + " : Resending SessionRequest message." );

            SendMessage(
                SSUHeader.MessageTypes.SessionRequest,
                Session.IntroKey,
                Session.IntroKey,
                ( start, writer ) =>
                {
                    writer.Write( X.Key.ToByteArray() );
                    var addr = Session.RemoteEP.Address.GetAddressBytes();
                    writer.Write8( (byte)addr.Length );
                    writer.Write( addr );

                    return true;
                } );
        }

    }
}
