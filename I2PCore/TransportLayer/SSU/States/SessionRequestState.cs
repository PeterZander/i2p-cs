using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;
using I2PCore.SessionLayer;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Data;
using I2PCore.TransportLayer.SSU.Data;

namespace I2PCore.TransportLayer.SSU
{
    public class SessionRequestState: SSUState
    {
        internal I2PPrivateKey PrivateKey;
        internal I2PPublicKey X;
        internal I2PPublicKey Y;

        internal SessionCreated SCMessage;

        protected readonly bool RemoteIsFirewalled;

        public SessionRequestState( SSUSession sess, bool remoteisfirewalled ): base( sess )
        {
            RemoteIsFirewalled = remoteisfirewalled;

            var keys = I2PPrivateKey.GetNewKeyPair();
            PrivateKey = keys.PrivateKey;
            X = keys.PublicKey;
        }

        protected override BufLen CurrentMACKey { get { return Session.RemoteIntroKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.RemoteIntroKey; } }

        PeriodicAction ResendSessionRequestAction = new PeriodicAction( HandshakeStateTimeout / 2, true );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeout * 4 ) )
            {
                Session.Host.EPStatisitcs.ConnectionTimeout( Session.RemoteEP );
                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                throw new FailedToConnectException( $"SSU {this}: Failed to connect. Timeout." );
            }

            ResendSessionRequestAction.Do( () =>
            {
                if ( ++Retries > HandshakeStateMaxRetries )
                    throw new FailedToConnectException( $"SSU {this}: Failed to connect. Too many retries." );

                SendSessionRequest();
            } );

            return this;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            var tstime = SSUHost.SSUDateTime( header.TimeStamp );

            if ( header.MessageType != SSUHeader.MessageTypes.SessionCreated )
            {
                Logging.LogTransport( $"SSU SessionRequestState: Received unexpected message {tstime} : {header.Flag}" );
                return this;
            }

            SCMessage = new SessionCreated( reader, Session.RemoteRouter.Certificate );

            Session.RelayTag = SCMessage.RelayTag;

            Y = new I2PPublicKey( (BufRefLen)SCMessage.Y, Session.RemoteRouter.Certificate );
            BufUtils.DHI2PToSessionAndMAC( out var sessionkey, out var mackey,
                Y.ToBigInteger().ModPow( PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

            Session.SharedKey = sessionkey;
            Session.MACKey = mackey;

            var ipaddr = new IPAddress( SCMessage.Address.ToByteArray() );
            ushort port = SCMessage.Port.PeekFlip16( 0 );
            Session.SignOnTimeB = SCMessage.SignOnTime.Peek32( 0 );
            var btime = SSUHost.SSUDateTime( BufUtils.Flip32( Session.SignOnTimeB ) );

            Logging.LogTransport( $"SSU SessionRequestState {Session.DebugId} : Received SessionCreated. {tstime.ToString()} : {btime}" );
            Session.Host.ReportedAddress( ipaddr );

            if ( !I2PSignature.SupportedSignatureType( Session.RemoteRouter.Certificate.SignatureType ) )
                throw new SignatureCheckFailureException( $"SSU SessionRequestState {Session.DebugId} : " +
                    $"Received non supported signature type: " +
                    $"{Session.RemoteRouter.Certificate.SignatureType}" );

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

            Logging.LogTransport( $"SSU SessionRequestState: Signature check: {sok}. {Session.RemoteRouter.Certificate.SignatureType}" );

            if ( !sok )
            {
                throw new SignatureCheckFailureException( $"SSU SessionRequestState {Session.DebugId}: Received SessionCreated signature check failed." +
                    Session.RemoteRouter.Certificate.ToString() );
            }

            if ( !RemoteIsFirewalled )
            {
                var relaytag = SCMessage.RelayTag.PeekFlip32( 0 );
                if ( relaytag != 0 )
                {
                    Session.RemoteIntroducerInfo = new IntroducerInfo(
                            Session.RemoteEP.Address,
                            (ushort)Session.RemoteEP.Port,
                            Session.RemoteIntroKey, relaytag );

                    Session.Host.IntroductionRelayOffered( Session.RemoteIntroducerInfo );
                }
            }

            Logging.LogTransport( $"SSU {this}: SessionCreated received " +
            	$"from {Session.RemoteEP} created. Moving to SessionConfirmedState." );

            Session.ReportConnectionEstablished();

            return new SessionConfirmedState( Session, this );
        }

        private void SendSessionRequest()
        {
            Logging.LogTransport( $"SSU SessionRequestState {Session.DebugId}: " +
                $"sending SessionRequest message to {Session.RemoteEP}." );

            SendMessage(
                SSUHeader.MessageTypes.SessionRequest,
                Session.RemoteIntroKey,
                Session.RemoteIntroKey,
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
