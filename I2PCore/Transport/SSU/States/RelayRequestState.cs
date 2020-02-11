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
    public class RelayRequestState : SSUState
    {
        public const int RelayRequestStateTimeoutSeconds = 2;
        public const int RelayRequestStateMaxRetries = 2;

        uint Nonce;

        readonly Dictionary<IntroducerInfo, SSUSession> Introducers;

        internal RelayRequestState( SSUSession sess, Dictionary<IntroducerInfo, SSUSession> introducers ): base( sess )
        {
            if ( !introducers.Any() )
            {
                if ( Session.RemoteEP != null )
                {
                    Logging.LogTransport( $"SSU RelayRequestState {Session.DebugId} no established sessions to introducers. Trying direct connect." );
                    NextState = new SessionRequestState( Session, false );
                }
                else
                {
                    throw new FailedToConnectException( $"SSU RelayRequestState {Session.DebugId} no established sessions to introducers" );
                }
            }

            Introducers = introducers;

            foreach ( var one in introducers )
            {
                Logging.LogInformation( $"RelayRequestState {Session.DebugId} " +
                    $"Trying {one.Key.EndPoint} to reach " +
                    $"{Session.RemoteAddr.Options.TryGet( "host" )?.ToString() ?? Session.RemoteAddr.Options.ToString()}" );
            }
            Session.Host.RelayResponseReceived += new SSUHost.RelayResponseInfo( Host_RelayResponseReceived );
        }

        PeriodicAction ResendRelayRequestAction = new PeriodicAction( TickSpan.Seconds( HandshakeStateTimeoutSeconds / 5 ), false );

        SSUState NextState = null;

        public override SSUState Run()
        {
            if ( NextState != null )
            {
                Session.Host.RelayResponseReceived -= Host_RelayResponseReceived; 
                return NextState;
            }

            if ( Timeout( HandshakeStateTimeoutSeconds ) )
            {
                Session.Host.RelayResponseReceived -= Host_RelayResponseReceived; 
                throw new FailedToConnectException( $"SSU RelayRequestState {Session.DebugId} Failed to connect. Timeout." );
            }

            ResendRelayRequestAction.Do( () =>
            {
                if ( ++Retries > RelayRequestStateMaxRetries )
                {
                    Session.Host.RelayResponseReceived -= Host_RelayResponseReceived;
                    throw new FailedToConnectException( $"SSU {Session.DebugId} RelayRequestState, too many retries." );
                }

                SendRelayRequest();
            } );

            return this;
        }

        private void SendRelayRequest()
        {
            foreach ( var one in Introducers )
            {
                var introducer = one.Key;
                var isession = one.Value;

                Logging.LogTransport( $"SSU RelayRequestState: {Session.DebugId} Sending RelayRequest to {introducer.EndPoint}" );

                SendMessage(
                    isession.RemoteEP,
                    SSUHeader.MessageTypes.RelayRequest,
                    isession.MACKey,
                    isession.SharedKey,
                    ( start, writer ) =>
                    {
                        writer.WriteFlip32( introducer.IntroTag );

                        // The IP address is only included if it is be different than the packet's 
                        // source address and port. In the current implementation, the IP length is 
                        // always 0 and the port is always 0, and the receiver should use the 
                        // packet's source address and port. https://geti2p.net/spec/ssu
                        writer.Write8( 0 );
                        writer.Write16( 0 );

                        // Challenge is unimplemented, challenge size is always zero. https://geti2p.net/spec/ssu
                        writer.Write8( 0 );

                        writer.Write( Session.MyRouterContext.IntroKey );

                        Nonce = BufUtils.RandomUint();
                        writer.Write32( Nonce );

                        return true;
                    } );
            }
        }

        protected override BufLen CurrentMACKey { get { return Session.MyRouterContext.IntroKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.MyRouterContext.IntroKey; } }

        void Host_RelayResponseReceived( SSUHeader header, RelayResponse response, IPEndPoint ep )
        {
            Logging.LogTransport(
                        $"SSU RelayRequestState: {Session.DebugId} RelayResponse from {ep} received." );

            if ( header.MessageType == SSUHeader.MessageTypes.RelayResponse )
            {
                if ( Introducers.Any( i => i.Value.RemoteEP == ep ) )
                {
                    HandleRelayResponse( response );
                }
                else
                {
                    Logging.LogTransport( 
                        $"SSU RelayRequestState: {Session.DebugId} RelayResponse from {ep} received. " +
                        $"Ignored as not in wait set [{string.Join( ", ", Introducers.Select( i => i.Value.RemoteEP ) )}]." );
                }
            }
        }

        SSUState HandleRelayResponse( RelayResponse response )
        {
            var cep = response.CharlieEndpoint;
            Logging.LogTransport( $"SSU RelayRequestState: {Session.DebugId} RelayResponse {response}" );

            var noncematch = Nonce == response.Nonce.Peek32( 0 );
            Logging.LogTransport( $"SSU RelayRequestState: {Session.DebugId} Nonce match: {noncematch}" );
            if ( !noncematch ) return this;

            Session.RemoteEP = response.CharlieEndpoint;
            Session.Host.RelayResponseReceived -= Host_RelayResponseReceived;

            NextState = new SessionRequestState( Session, true );
            return NextState;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
#if LOG_ALL_TRANSPORT
            Logging.LogTransport( $"SSU RelayRequestState: {Session.DebugId} Received {header.MessageType}" );
#endif

            if ( header.MessageType == SSUHeader.MessageTypes.RelayResponse )
            {
                var response = new RelayResponse( reader );
                return HandleRelayResponse( response );
            }

            return this;
        }
    }
}
