using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using I2PCore.Utils;
using I2PCore.Router;
using I2PCore.Data;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace I2PCore.Transport.SSU
{
    class SessionCreatedState: SSUState
    {
        internal I2PPrivateKey PrivateKey;
        internal I2PPublicKey Y;
        internal SessionRequest Request;
        
        uint RelayTag = 0;
        byte[] AAddr;
        ushort APort;
        uint ASignonTime;
        I2PSignature ASign;

        public SessionCreatedState( SSUSession sess )
            : base( sess )
        {
            var keys = I2PPrivateKey.GetNewKeyPair();
            PrivateKey = keys.PrivateKey;
            Y = keys.PublicKey;
        }

        protected override BufLen CurrentMACKey { get { return Request == null ? Session.MyRouterContext.IntroKey : Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Request == null ? Session.MyRouterContext.IntroKey : Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            switch ( header.MessageType )
            {
                case SSUHeader.MessageTypes.SessionRequest:
                    var req = new SessionRequest( reader, I2PPublicKey.DefaultAsymetricKeyCert );
                    DebugUtils.Log( "SSU SessionCreatedState " + Session.DebugId + " : OK SessionRequest received." );

                    BufUtils.DHI2PToSessionAndMAC( out Session.SharedKey, out Session.MACKey,
                        req.XKey.ModPow( PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

                    Session.Host.ReportedAddress( new IPAddress( req.Address.ToByteArray() ) );

                    // TODO: Remove comment when relaying is implemented
                    /*
                    if ( header.ExtendedOptions != null )
                    {
                        if ( header.ExtendedOptions.Length == 2 && ( ( header.ExtendedOptions[0] & 0x01 ) != 0 ) )
                        {
                            RelayTag = BufUtils.RandomUint();
                        }
                    }*/

                    Request = req;
                    SendSessionCreated();

                    return this;

                case SSUHeader.MessageTypes.RelayResponse:
                    DebugUtils.LogDebug( () => string.Format( "SSU SessionCreatedState {0}: RelayResponse received from {1}.",
                        Session.DebugId, ( Session.RemoteEP == null ? "<null>" : Session.RemoteEP.ToString() ) ) );
                    var response = new RelayResponse( reader );
                    Session.Host.ReportRelayResponse( header, response, Session.RemoteEP );
                    break;

                case SSUHeader.MessageTypes.SessionConfirmed:
                    return ParseSessionConfirmed( header, reader );

                case SSUHeader.MessageTypes.PeerTest:
                    HandleIncomingPeerTestPackage( reader );
                    break;

                default:
                    DebugUtils.Log( "SSU SessionCreatedState: Session " + Session.DebugId + " Unexpected Message: " + header.MessageType.ToString() );
                    break;
            }

            return this;
        }

        PeriodicAction ResendSessionCreatedAction = new PeriodicAction( TickSpan.Seconds( HandshakeStateTimeoutSeconds / 5 ), false );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeoutSeconds ) )
            {
                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                throw new FailedToConnectException( "SSU " + Session.DebugId + " Failed to connect. Timeout." );
            }

            if ( Request == null ) return this;

            ResendSessionCreatedAction.Do( () => 
                {
                    if ( ++Retries > HandshakeStateMaxRetries ) 
                        throw new FailedToConnectException( "SSU " + Session.DebugId + " Failed to connect. Too many retries." );

                    DebugUtils.Log( "SSU SessionCreatedState " + Session.DebugId + " : Resending SessionCreated message." );
                    SendSessionCreated();
                });

            return this;
        }

        private void SendSessionCreated()
        {
            SendMessage(
                SSUHeader.MessageTypes.SessionCreated,
                Session.MyRouterContext.IntroKey,
                Session.MyRouterContext.IntroKey,
                ( start, writer ) =>
                {
                    writer.Write( Y.Key );
                    AAddr = Session.RemoteEP.Address.GetAddressBytes();
                    writer.Write8( (byte)AAddr.Length );
                    writer.Write( AAddr );
                    APort = BufUtils.Flip16( (ushort)Session.RemoteEP.Port );
                    writer.Write16( APort );
                    writer.WriteFlip32( RelayTag );
                    Session.SignOnTimeB = BufUtils.Flip32( SSUHost.SSUTime( DateTime.UtcNow ) );
                    writer.Write32( Session.SignOnTimeB );

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                        Request.X, Y.Key, 
                        new BufLen( AAddr ), (BufLen)APort,
                        Request.Address, (BufLen)BufUtils.Flip16( (ushort)Session.MyRouterContext.UDPPort ),
                        (BufLen)RelayTag, (BufLen)Session.SignOnTimeB );

                    var signstart = new BufLen( writer );
                    writer.Write( sign );
                    var padding = BufUtils.Get16BytePadding( writer - signstart );
                    writer.Write( BufUtils.Random( padding ) );

                    var cipher = new CbcBlockCipher( new AesEngine() ); 
                    var signcryptbuf = new BufLen( signstart, 0, writer - signstart );
                    cipher.Init( true, Session.SharedKey.ToParametersWithIV( new BufLen( start, 16, 16 ) ) );
                    cipher.ProcessBytes( signcryptbuf );
                    return true;
                } );
        }

        List<BufLen> Fragments = null;

        private SSUState ParseSessionConfirmed( SSUHeader header, BufRefLen reader )
        {
            var info = reader.Read8();
            var cursize = reader.ReadFlip16();

            if ( Fragments != null ) return AssembleFragments( header, reader, info, cursize );

            if ( ( info & 0x0f ) == 1 )
            {
                var cursizedata = reader.ReadBufRefLen( cursize );
                Session.RemoteRouter = new I2PRouterIdentity( cursizedata );
                ASignonTime = reader.Read32();
                reader.Seek( reader.Length - Session.RemoteRouter.Certificate.SignatureLength );
                ASign = new I2PSignature( reader, Session.RemoteRouter.Certificate );

                return VerifyRemoteSignature();
            }

            Fragments = new List<BufLen>( new BufLen[info & 0x0f] );
            return AssembleFragments( header, reader, info, cursize );
        }

        private SSUState VerifyRemoteSignature()
        {
            var baaddr = new BufLen( AAddr );
            var bbport = BufUtils.Flip16BL( (ushort)Session.MyRouterContext.UDPPort );

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( string.Format( "SSU SessionCreatedState {0}: X for signature {1}.",
                Session.DebugId, Request.X ) );
            DebugUtils.Log( string.Format( "SSU SessionCreatedState {0}: Y for signature {1}.",
                Session.DebugId, Y.Key ) );
            DebugUtils.Log( string.Format( "SSU SessionCreatedState {0}: Alice address for signature {1}. Port {2}.",
                Session.DebugId, baaddr, (BufLen)APort ) );
            DebugUtils.Log( string.Format( "SSU SessionCreatedState {0}: Bob address for signature {1}. Port {2}.",
                Session.DebugId, Request.Address, bbport ) );
            DebugUtils.Log( string.Format( "SSU SessionCreatedState {0}: Relay tag {1}. Signon time {2}.",
                Session.DebugId, (BufLen)RelayTag, (BufLen)ASignonTime ) );
#endif

            var signdata = new BufLen[] {
                    Request.X, Y.Key, 
                    baaddr, (BufLen)APort,
                    Request.Address, bbport,
                    (BufLen)RelayTag, (BufLen)ASignonTime 
                };
            var ok = I2PSignature.DoVerify( Session.RemoteRouter.SigningPublicKey, ASign, signdata );
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SSU SessionCreatedState " + Session.DebugId + ": " + 
                Session.RemoteRouter.Certificate.SignatureType.ToString() + 
                " signature check: " + ok.ToString() );
#endif
            if ( !ok ) throw new SignatureCheckFailureException( "SSU SessionCreatedState recv sig check failure" );

            DebugUtils.Log( "SSU SessionCreatedState: Session " + Session.DebugId + " established. Moving to Established state." );
            var next = new EstablishedState( Session );

            Session.ReportConnectionEstablished();

            if ( NetDb.Inst != null ) NetDb.Inst.Statistics.SuccessfulConnect( Session.RemoteRouter.IdentHash );
            return next;
        }

        private SSUState AssembleFragments( SSUHeader header, BufRefLen reader, byte info, ushort cursize )
        {
            var fragnr = info >> 4;
            var fragcount = info & 0x0f;
            if ( fragnr == fragcount - 1 )
            {
                ASignonTime = reader.Read32();
                reader.Seek( reader.Length - Session.RemoteRouter.Certificate.SignatureLength );
                ASign = new I2PSignature( reader, Session.RemoteRouter.Certificate );
            }
            else
            {
                Fragments[fragnr] = reader.ReadBufLen( cursize );
            }

            if ( Fragments.Any( f => f == null ) ) return this;

            Session.RemoteRouter = new I2PRouterIdentity( new BufRefLen( Fragments.SelectMany( f => f.ToByteArray() ).ToArray() ) );

            return VerifyRemoteSignature();
        }

        private void HandleIncomingPeerTestPackage( BufRefLen reader )
        {
            // We are Alice or Charlie and are receiving probe packet with intro key.

            var msg = new PeerTest( reader );

            if ( Session.Host.PeerTestInstance.OurTestNonce( msg.TestNonce ) )
            {
                // We are alice running a test
#if NO_LOG_ALL_TRANSPORT
                DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Alice, and are getting a direct probe from Charlie. " + msg.ToString() );
#endif
                Session.Host.PeerTestInstance.CharlieDirectResponseReceived( msg );
                return;
            }

            var nonceinfo = Session.Host.GetNonceInfo( msg.TestNonce.Peek32( 0 ) );
            if ( nonceinfo == null )
            {
                DebugUtils.LogDebug( "SSU PeerTest " + Session.DebugId + " Created state: HandleIncomingPeerTestPackage received an unknown nonce. Dropped." );
                return;
            }

            if ( nonceinfo.Role == PeerTestRole.Charlie )
            {
                // We are Charlie and are getting a direct probe from Alice

                SendMessage(
                    SSUHeader.MessageTypes.PeerTest,
                    msg.IntroKey,
                    msg.IntroKey,
                    ( start, writer ) =>
                    {
                        var toalice = new PeerTest( msg.TestNonce, msg.AliceIPAddr, msg.AlicePort, Session.MyRouterContext.IntroKey );
#if NO_LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Charlie, and are getting a direct probe from Alice. " + toalice.ToString() );
#endif
                        toalice.WriteTo( writer );

                        return true;
                    } );
            }
            else
            {
                DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": Unexpected PeerTest received: " + msg.ToString() );
            }
        }
    }
}
