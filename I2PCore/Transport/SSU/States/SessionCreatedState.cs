using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using I2PCore.Data;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;

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

            Session.MACKey = Session.MyRouterContext.IntroKey;
            Session.SharedKey = Session.MyRouterContext.IntroKey;
        }

        protected override BufLen CurrentMACKey { get { return Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            switch ( header.MessageType )
            {
                case SSUHeader.MessageTypes.SessionRequest:
                    var req = new SessionRequest( reader, I2PKeyType.DefaultAsymetricKeyCert );
                    Logging.LogTransport( $"SSU SessionCreatedState {Session.DebugId}: OK SessionRequest received." );

                    BufUtils.DHI2PToSessionAndMAC( out var sessionkey, out var mackey,
                        req.XKey.ModPow( PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

                    Session.MACKey = mackey;
                    Session.SharedKey = sessionkey;

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
                    Logging.LogTransport( string.Format( "SSU SessionCreatedState {0}: RelayResponse received from {1}.",
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
                    Logging.LogTransport( $"SSU SessionCreatedState: Session {Session.DebugId} Unexpected Message: {header.MessageType}" );
                    break;
            }

            return this;
        }

        PeriodicAction ResendSessionCreatedAction = new PeriodicAction( HandshakeStateTimeout / 3, false );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeout * 2 ) )
            {
                Session.Host.EPStatisitcs.ConnectionTimeout( Session.RemoteEP );

                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                Session.Host.ReportEPProblem( Session.RemoteEP );

                throw new FailedToConnectException( $"SSU {Session.DebugId} Failed to connect. Timeout." );
            }

            if ( Request == null ) return this;

            ResendSessionCreatedAction.Do( () => 
                {
                    if ( ++Retries > HandshakeStateMaxRetries ) 
                        throw new FailedToConnectException( $"SSU {Session.DebugId} Failed to connect. Too many retries." );

                    Logging.LogTransport( $"SSU SessionCreatedState {Session.DebugId} : Resending SessionCreated message." );
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

        BufLen[] Fragments = null;

        private SSUState ParseSessionConfirmed( SSUHeader header, BufRefLen reader )
        {
            var info = reader.Read8();
            var cursize = reader.ReadFlip16();

            if ( Fragments is null )
            {
                Fragments = new BufLen[info & 0x0f];
            }

            return AssembleFragments( header, reader, info, cursize );
        }

        private SSUState VerifyRemoteSignature()
        {
            var baaddr = new BufLen( AAddr );
            var bbport = BufUtils.Flip16BL( (ushort)Session.MyRouterContext.UDPPort );

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( $"SSU {this}: X for signature {Request.X}." );
            Logging.LogTransport( $"SSU {this}: Y for signature {Y.Key}." );
            Logging.LogTransport( $"SSU {this}: Alice address for signature {baaddr}. Port {(BufLen)APort}." );
            Logging.LogTransport( $"SSU {this}: Bob address for signature {Request.Address}. Port {bbport}." );
            Logging.LogTransport( $"SSU {this}: Relay tag {(BufLen)RelayTag}. Signon time {(BufLen)ASignonTime}." );
#endif

            var signdata = new BufLen[] {
                    Request.X, Y.Key, 
                    baaddr, (BufLen)APort,
                    Request.Address, bbport,
                    (BufLen)RelayTag, (BufLen)ASignonTime 
                };

            var ok = I2PSignature.DoVerify( Session.RemoteRouter.SigningPublicKey, ASign, signdata );

            Logging.LogTransport( $"SSU SessionCreatedState {Session.DebugId}: " + 
                $"{Session.RemoteRouter.Certificate.SignatureType} " + 
                $"signature check: {ok}" );

            if ( !ok ) throw new SignatureCheckFailureException( "SSU SessionCreatedState recv sig check failure" );

            Logging.LogTransport( "SSU SessionCreatedState: Session " + Session.DebugId + " established. Moving to Established state." );
            var next = new EstablishedState( Session );

            Session.ReportConnectionEstablished();

            NetDb.Inst.Statistics.SuccessfulConnect( Session.RemoteRouter.IdentHash );
            return next;
        }

        private SSUState AssembleFragments( SSUHeader header, BufRefLen reader, byte info, ushort cursize )
        {
            var fragnr = info >> 4;
            var fragcount = info & 0x0f;

            Logging.LogTransport( $"AssembleFragments: frag {fragnr} / {fragcount}, len {cursize}." );

            if ( fragnr != fragcount - 1 )
            {
                Fragments[fragnr] = reader.ReadBufLen( cursize );
            }
            else
            {
                ASignonTime = reader.Peek32( cursize );
                Fragments[fragnr] = reader.ReadBufLen( reader.Length );
            }

            if ( Fragments.Any( f => f is null ) ) return this;

            var buf = new BufLen( new byte[Fragments.Sum( f => f.Length )] );
            var bufwriter = new BufRefLen( buf );
            for ( int i = 0; i < Fragments.Length; ++i )
            {
                bufwriter.Write( Fragments[i] );
            }
            Session.RemoteRouter = new I2PRouterIdentity( (BufRefLen)buf );

            var signbuf = new BufRefLen( buf,
                buf.Length - Session.RemoteRouter.Certificate.SignatureLength );
            ASign = new I2PSignature( signbuf, Session.RemoteRouter.Certificate );

            return VerifyRemoteSignature();
        }

        private void HandleIncomingPeerTestPackage( BufRefLen reader )
        {
            // We are Alice or Charlie and are receiving probe packet with intro key.

            var msg = new PeerTest( reader );

            if ( Session.Host.PeerTestInstance.OurTestNonce( msg.TestNonce ) )
            {
                // We are alice running a test
                Logging.LogTransport( $"SSU {this}: PeerTest. We are Alice, and are getting a direct probe from Charlie. {msg}" );
                Session.Host.PeerTestInstance.CharlieDirectResponseReceived( msg );
                return;
            }

            var nonceinfo = Session.Host.GetNonceInfo( msg.TestNonce.Peek32( 0 ) );
            if ( nonceinfo == null )
            {
                Logging.LogTransport( $"SSU {this}: PeerTest. Created state: HandleIncomingPeerTestPackage received an unknown nonce. Dropped." );
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
                        Logging.LogTransport( $"SSU {this}: PeerTest. We are Charlie, and are getting a direct probe from Alice. {msg}" );
                        toalice.WriteTo( writer );

                        return true;
                    } );
            }
            else
            {
                Logging.LogTransport( "SSU {this}: PeerTest. Unexpected PeerTest received: {msg}" );
            }
        }
    }
}
