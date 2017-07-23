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

        IList<IntroducerInfo> Introducers;
        IntroducerInfo CurrentIntroducer;

        internal RelayRequestState( SSUSession sess, IList<IntroducerInfo> introducers ): base( sess )
        {
            Introducers = introducers;

            if ( Introducers.Count == 0 )
            {
                throw new FailedToConnectException( "SSU +{Session.TransportInstance}+ Failed to find a non established introducer" );
            }
            else
            {
                CurrentIntroducer = Introducers[0];
                Introducers.RemoveAt( 0 );

                Session.Host.RelayResponseReceived += new SSUHost.RelayResponseInfo( Host_RelayResponseReceived );
            }
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
                throw new FailedToConnectException( "SSU RelayRequestState {Session.DebugId} Failed to connect. Timeout." );
            }

            ResendRelayRequestAction.Do( () =>
            {
                if ( ++Retries > RelayRequestStateMaxRetries )
                {
                    DebugUtils.LogDebug( $"SSU RelayRequestState: {Session.DebugId}" +
                        $" Using introducer '{CurrentIntroducer.EndPoint}' timed out." );

                    if ( Introducers.Count == 0 )
                    {
                        Session.Host.RelayResponseReceived -= Host_RelayResponseReceived;
                        throw new FailedToConnectException( $"SSU +{Session.TransportInstance}+ Failed to find a non established introducer" );
                    }
                    else
                    {
                        CurrentIntroducer = Introducers[0];
                        Introducers.RemoveAt( 0 );

                        DebugUtils.LogDebug( $"SSU RelayRequestState: +{Session.TransportInstance}+ " +
                            $"Trying introducer '{CurrentIntroducer.EndPoint}' next." );

                        Retries = 0;
                    }
                }

                SendRelayRequest();
            } );

            return this;
        }

        private void SendRelayRequest()
        {
            if ( CurrentIntroducer == null ) return;

            DebugUtils.LogDebug( $"SSU RelayRequestState: {Session.DebugId} Sending RelayRequest to {CurrentIntroducer.EndPoint}" );

            SendMessage(
                CurrentIntroducer.EndPoint,
                SSUHeader.MessageTypes.RelayRequest,
                CurrentIntroducer.IntroKey,
                CurrentIntroducer.IntroKey,
                ( start, writer ) =>
                {
                    writer.WriteFlip32( CurrentIntroducer.IntroTag );

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

        protected override BufLen CurrentMACKey { get { return Session.MyRouterContext.IntroKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.MyRouterContext.IntroKey; } }

        void Host_RelayResponseReceived( SSUHeader header, RelayResponse response, IPEndPoint ep )
        {
            if ( header.MessageType == SSUHeader.MessageTypes.RelayResponse )
            {
                if ( ep.Address.Equals( CurrentIntroducer.EndPoint.Address ) )
                {
                    HandleRelayResponse( response );
                }
                else
                {
                    DebugUtils.LogDebug( () =>  
                        $"SSU RelayRequestState: {Session.DebugId} RelayResponse from {ep.Address} received. Waiting for response from {CurrentIntroducer.EndPoint.Address}." );
                }
            }
        }

        SSUState HandleRelayResponse( RelayResponse response )
        {
            var cep = response.CharlieEndpoint;
            DebugUtils.LogDebug( $"SSU RelayRequestState: {Session.DebugId} RelayResponse {response}" );

            var noncematch = Nonce == response.Nonce.Peek32( 0 );
            DebugUtils.LogDebug( $"SSU RelayRequestState: {Session.DebugId} Nonce match: {noncematch}" );
            if ( !noncematch ) return this;

            Session.RemoteEP = response.CharlieEndpoint;
            Session.Host.RelayResponseReceived -= Host_RelayResponseReceived;

            NextState = new SessionRequestState( Session );
            return NextState;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
#if LOG_ALL_TRANSPORT
            DebugUtils.LogDebug( $"SSU RelayRequestState: {Session.DebugId} Received {header.MessageType}" );
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
