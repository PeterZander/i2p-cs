#define NO_SSU_SEND_KEEPALIVE

using System;
using System.Collections.Generic;
using I2PCore.Utils;
using I2PCore.Data;
using System.Net;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;

namespace I2PCore.TransportLayer.SSU
{
    public class EstablishedState: SSUState
    {
        protected readonly TickSpan IntroducerKeepaliveTimeout = TickSpan.Seconds( 1.5 );
        protected readonly TickSpan NonIntroducerKeepaliveTimeout = TickSpan.Seconds( 3.5 );
        protected readonly TickSpan NotFirewalledKeepaliveTimeout = TickSpan.Seconds( 15 );

        public EstablishedState( SSUSession sess )
            : base( sess )
        {
        }

        protected override BufLen CurrentMACKey { get { return Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            switch ( header.MessageType )
            {
                case SSUHeader.MessageTypes.Data:
                    try
                    {
                        var datamsg = new SSUDataMessage( reader, Session.Defragmenter );
                        if ( datamsg.ExplicitAcks != null ) Session.Fragmenter.GotAck( datamsg.ExplicitAcks );
                        if ( datamsg.AckBitfields != null ) Session.Fragmenter.GotAck( datamsg.AckBitfields );
                        if ( datamsg.NewMessages != null )
                        {
                            foreach ( var msg in datamsg.NewMessages )
                            {
                                var i2npmsg = I2NPMessage.ReadHeader16( (BufRefLen)msg.GetPayload() );

#if LOG_MUCH_TRANSPORT
                                Logging.LogDebugData( $"SSU {this} complete message " + 
                                    $"{msg.MessageId}: {i2npmsg.Expiration}" );
#endif

                                Session.MessageReceived( i2npmsg );
                            }
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( "EstablishedState: SSUHost.SSUMessageTypes.Data", ex );
                    }
                    break;

                case SSUHeader.MessageTypes.PeerTest:
                    HandleIncomingPeerTestPackage( reader );
                    break;

                case SSUHeader.MessageTypes.RelayResponse:
                    Logging.LogTransport( $"SSU EstablishedState {Session.DebugId}: RelayResponse received from {Session.RemoteEP}." );
                    var response = new RelayResponse( reader );
                    Session.Host.ReportRelayResponse( header, response, Session.RemoteEP );
                    break;

                case SSUHeader.MessageTypes.RelayIntro:
                    var intro = new RelayIntro( reader );
                    Logging.LogTransport( $"SSU EstablishedState {Session.DebugId}: RelayIntro received from {Session.RemoteEP} for {intro.AliceEndpoint}." );

                    var data = Session.Host.SendBuffers.Pop( 25 + BufUtils.RandomInt( 15 ) );
                    data.Randomize();
                    Send( intro.AliceEndpoint, data );

                    ++Session.Host.EPStatisitcs[Session.RemoteEP].RelayIntrosReceived;
                    ++Session.RelayIntroductionsReceived;
                    break;
                
                case SSUHeader.MessageTypes.RelayRequest:
                    // TODO: Implement
                    // if ( !SSUHost.IntroductionSupported ) throw new Exception( "SSU relay introduction not supported" );
                    Logging.LogTransport( string.Format( "SSU EstablishedState {0}: Relay introduction not supported.", Session.DebugId ) );
                    break;

                case SSUHeader.MessageTypes.SessionRequest:
                    Logging.LogTransport( string.Format( "SSU EstablishedState {0}: SessionRequest received. Ending session.", Session.DebugId ) );
                    SendSessionDestroyed();
                    return null;

                default:
                    Logging.LogTransport( string.Format( "SSU EstablishedState {0}: Unexpected message received: {1}.",
                        Session.DebugId, header.MessageType ) );
                    break;
            }

            return this;
        }

        PeriodicAction ResendFragmentsAction = new PeriodicAction( TickSpan.Seconds( 1 ) );
        PeriodicAction UpdateSessionLengthStats = new PeriodicAction( TickSpan.Minutes( 5 ) );
        TickCounter LastIntroducerKeepalive = TickCounter.Now;

        public override SSUState Run()
        {
            // Idle
            if ( Timeout( Session.IsIntroducerConnection ? InactivityTimeout * 3 : InactivityTimeout ) )
            {
                Logging.LogTransport( $"SSU EstablishedState {Session.DebugId}: " +
                    $"Inactivity timeout. Sending SessionDestroyed. " +
                    $"s/r {LastSend.DeltaToNow} / {LastReceive.DeltaToNow}" );
                SendSessionDestroyed();
                return null;
            }

            UpdateSessionLengthStats.Do( () =>
                Session.Host.EPStatisitcs.UpdateSessionLength(
                    Session.RemoteEP,
                    Session.StartTime.DeltaToNow ) );

            var dosend = Session.Defragmenter.GotAcks 
                    || ( !Session.SendQueue.IsEmpty )
                    || Session.Fragmenter.GotUnsentFragments;

            TickSpan timeout = null;

            if ( !dosend )
            {
                if ( SessionLayer.RouterContext.Inst.IsFirewalled )
                {
                    if ( Session.IsIntroducerConnection )
                    {
                        timeout = IntroducerKeepaliveTimeout;
                    }
                    else
                    {
                        timeout = NonIntroducerKeepaliveTimeout;
                    }
                }
                else
                {
                    timeout = NotFirewalledKeepaliveTimeout;
                }
            }

#if SSU_SEND_KEEPALIVE
            if ( !dosend && LastIntroducerKeepalive.DeltaToNow > timeout )
            {
                dosend = true;

#if LOG_MUCH_TRANSPORT
                Logging.LogTransport( $"SSU EstablishedState {Session.DebugId}:" +
                    $"{(Session.IsIntroducerConnection ? " Introducer" : "")} keepalive." );
#endif
                LastIntroducerKeepalive.SetNow();
            }
#endif

            if ( dosend )
            {
                do
                {
                    SendAcksAndData();
                } while ( !Session.SendQueue.IsEmpty );
            }

            ResendFragmentsAction.Do( delegate
            {
                if ( Session.Fragmenter.AllFragmentsAcked ) return;

                ResendNotAcked( Session.Fragmenter.NotAckedFragments() );
            } );

            if ( Session.QueuedFirstPeerTestToCharlie != null )
            {
                SendFirstPeerTestToCharlie( Session.QueuedFirstPeerTestToCharlie );
                Session.QueuedFirstPeerTestToCharlie = null;
            }

            return this;
        }

        private void ResendNotAcked( IEnumerable<DataFragment> fragments )
        {
            bool finished = false;
            var current = fragments.GetEnumerator();
            var i = 0;

            while ( !finished )
            {
                if ( ++i > 50 ) 
                        break;

                SendMessage(
                    SSUHeader.MessageTypes.Data,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        var flagbuf = writer.ReadBufLen( 1 );
                        flagbuf[0] = (byte)SSUDataMessage.DataMessageFlags.WantReply;

                        // Data
                        var fragcountbuf = writer.ReadBufLen( 1 );
                        int fragmentcount = 0;
                        while( current.MoveNext() )
                        {
                            if ( current.Current.Size > writer.Length
                                    || current.Current.Size == 0 ) break;

                            ++fragmentcount;

                            current.Current.WriteTo( writer );
#if LOG_MUCH_TRANSPORT
                            Logging.LogTransport( $"SSU resending fragment {current.Current.FragmentNumber} " +
                                $"of message {current.Current.MessageId}. IsLast: {current.Current.IsLast}" );
#endif
                        }

                        if ( fragmentcount == 0 )
                        {
                            finished = true;
                            return false;
                        }

                        fragcountbuf[0] = (byte)fragmentcount;

                        return true;
                    } );
            }
        }

        private void SendAcksAndData()
        {
            bool finished = false;
            int i = 0;

            while ( !finished )
            {
                if ( ++i > 50 ) 
                        break;

                SendMessage(
                    SSUHeader.MessageTypes.Data,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        // Acks
                        var flagbuf = writer.ReadBufLen( 1 );
                        flagbuf[0] = (byte)SSUDataMessage.DataMessageFlags.WantReply;
                        bool explacks, bitmaps;
                        Session.Defragmenter.SendAcks( writer, out explacks, out bitmaps );
                        if ( explacks ) flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.ExplicitAcks;
                        if ( bitmaps ) flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.BitfieldAcks;

                        // Data
                        var fragcountbuf = writer.ReadBufLen( 1 );
                        var fragments = Session.Fragmenter.Send( writer, Session.SendQueue );
                        fragcountbuf[0] = (byte)fragments;
                        finished = fragments == 0 && !explacks && !bitmaps;

                        return !finished;
                    } );
            }
        }

        private void HandleIncomingPeerTestPackage( BufRefLen reader )
        {
            var msg = new PeerTest( reader );
            var nonceinfo = Session.Host.GetNonceInfo( msg.TestNonce.Peek32( 0 ) );

            if ( msg.AliceIPAddr.Length == 0 && msg.AlicePort.Peek16( 0 ) == 0 )
            {
                // We are Bob, and we got the first message from Alice
                RespondToPeerTestInitiationFromAlice( msg, nonceinfo );
                return;
            }

            if ( !msg.IPAddressOk )
            {
                Logging.LogTransport( $"SSU {this}: HandleIncomingPeerTestPackage: IP Address not accepted. Ignorning. {msg}" );
                return;
            }

            if ( nonceinfo != null )
            {
                switch ( nonceinfo.Role )
                {
                    case PeerTestRole.Bob:
                        {
                            // Got initial response from Charlie
                            // Try to contact Alice
                            SendMessage(
                                new IPEndPoint( new IPAddress( msg.AliceIPAddr.ToByteArray() ), msg.AlicePort.PeekFlip16( 0 ) ),
                                SSUHeader.MessageTypes.PeerTest,
                                msg.IntroKey,
                                msg.IntroKey,
                                ( start, writer ) =>
                                {
                                    Logging.LogTransport( $"SSU {this}: PeerTest. We are Bob, relaying response from Charlie to Alice {msg}" );
                                    msg.WriteTo( writer );

                                    return true;
                                } );
                        }
                        return;

                    case PeerTestRole.Charlie:
                        {
                            // We got the final test from Alice
                            SendMessage(
                                SSUHeader.MessageTypes.PeerTest,
                                Session.MACKey,
                                Session.SharedKey,
                                ( start, writer ) =>
                                {
                                    Logging.LogTransport( $"SSU {this}: PeerTest. We are Charlie, responding to the final message from Alice {msg}" );
                                    msg.WriteTo( writer );

                                    return true;
                                } );
                        }
                        return;

                    default:
                        Logging.Log( $"SSU {this}: PeerTest. Unexpedted PeerTest received. {msg}" );
                        break;
                }
            }
            else
            {
                // We are Charlie receiving a relay from Bob.
                Session.Host.SetNonceInfo( msg.TestNonce.Peek32( 0 ), PeerTestRole.Charlie );

                // Reply to Bob
                SendMessage(
                    SSUHeader.MessageTypes.PeerTest,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        Logging.LogTransport( "SSU {this}: PeerTest. We are Charlie, responding to Bob. {msg}" );
                        msg.WriteTo( writer );

                        return true;
                    } );

                // Try to contact Alice
                var toalice = new PeerTest( msg.TestNonce, msg.AliceIPAddr, msg.AlicePort, Session.MyRouterContext.IntroKey );
                SendMessage(
                    new IPEndPoint( new IPAddress( msg.AliceIPAddr.ToByteArray() ), msg.AlicePort.PeekFlip16( 0 ) ),
                    SSUHeader.MessageTypes.PeerTest,
                    msg.IntroKey,
                    msg.IntroKey,
                    ( start, writer ) =>
                    {
                        Logging.LogTransport( $"SSU {this}: PeerTest. We are Charlie, sending first probe to Alice. {toalice}" );
                        toalice.WriteTo( writer );

                        return true;
                    } );
            }
        }

        private void RespondToPeerTestInitiationFromAlice( PeerTest msg, PeerTestNonceInfo nonceinfo )
        {
            // Nonce already in use for another test? Just ignore.
            if ( nonceinfo != null )
            {
                Logging.LogTransport( "SSU {this}: PeerTest. We are Bob getting a iniation from Alice, but will drop it due to nonce clash. {msg}" );
                return;
            }

            Session.Host.SetNonceInfo( msg.TestNonce.Peek32( 0 ), PeerTestRole.Bob );

            var pt = new PeerTest( msg.TestNonce,
                Session.UnwrappedRemoteAddress, Session.RemoteEP.Port,
                msg.IntroKey );

            Logging.LogTransport( $"SSU {this}: PeerTest. We are Bob and sending first relay to Charlie: {pt}" );
            Session.Host.SendFirstPeerTestToCharlie( pt );
            return;
        }

        void SendFirstPeerTestToCharlie( PeerTest msg )
        {
            SendMessage(
                SSUHeader.MessageTypes.PeerTest,
                Session.MACKey,
                Session.SharedKey,
                ( start, writer ) =>
                {
                    Logging.LogTransport( $"SSU {this}: PeerTest. We are Bob, relaying first message to Charlie. {msg}" );
                    msg.WriteTo( writer );

                    return true;
                } );
        }
    }
}
