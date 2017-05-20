using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Utils;
using I2PCore.Data;
using System.Net;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Transport.SSU
{
    public class EstablishedState: SSUState
    {
        public EstablishedState( SSUSession sess )
            : base( sess )
        {
        }

        protected override BufLen CurrentMACKey { get { return Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            DataSent();
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SSU EstablishedState +" + Session.TransportInstance.ToString() + "+ received: " + 
                header.MessageType.ToString() + ": " + SSUHost.SSUDateTime( header.TimeStamp ).ToString() );
#endif
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
                                var i2npmsg = I2NPMessage.ReadHeader5( (BufRefLen)msg.GetPayload() );

#if LOG_ALL_TRANSPORT
                            DebugUtils.Log( "SSU EstablishedState +" + Session.TransportInstance.ToString() + "+ complete message " + 
                                msg.MessageId.ToString() + ": " + i2npmsg.Expiration.ToString() );
#endif

                                if ( i2npmsg.MessageType == I2PCore.Tunnel.I2NP.Messages.I2NPMessage.MessageTypes.DeliveryStatus )
                                {
                                    if ( ( (DeliveryStatusMessage)i2npmsg.Message ).IsNetworkId( (ulong)I2PConstants.I2P_NETWORK_ID ) ) continue;
                                }

                                Session.MessageReceived( i2npmsg );
                            }
                        }
                    }
                    catch ( Exception ex )
                    {
                        DebugUtils.Log( "EstablishedState: SSUHost.SSUMessageTypes.Data", ex );
                    }
                    break;

                case SSUHeader.MessageTypes.SessionDestroyed:
                    DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: SessionDestroyed received.", Session.DebugId ) );
                    SendSessionDestroyed();
                    return null;

                case SSUHeader.MessageTypes.PeerTest:
                    HandleIncomingPeerTestPackage( reader );
                    break;

                case SSUHeader.MessageTypes.RelayResponse:
                    DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: RelayResponse received from {1}.", 
                        Session.DebugId, Session.RemoteEP ) );
                    var response = new RelayResponse( reader );
                    Session.Host.ReportRelayResponse( header, response, Session.RemoteEP );
                    break;

                case SSUHeader.MessageTypes.RelayRequest:
                case SSUHeader.MessageTypes.RelayIntro:
                    // if ( !SSUHost.IntroductionSupported ) throw new Exception( "SSU relay introduction not supported" );
                    DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: Relay introduction not supported.", Session.DebugId ) );
                    break;

                case SSUHeader.MessageTypes.SessionRequest:
                    DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: SessionRequest received. Ending session.", Session.DebugId ) );
                    SendSessionDestroyed();
                    return null;

                default:
                    DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: Unexpected message received: {1}.",
                        Session.DebugId, header.MessageType ) );
                    break;
            }

            return this;
        }

        PeriodicAction ResendFragmentsAction = new PeriodicAction( TickSpan.Seconds( 1 ) );

        public override SSUState Run()
        {
            // Idle
            if ( Timeout( InactivityTimeoutSeconds ) )
            {
                DebugUtils.LogDebug( () => string.Format( "SSU EstablishedState {0}: Inactivity timeout. Sending SessionDestroyed.", Session.DebugId ) );
                SendSessionDestroyed();
                return null;
            }

            if ( Session.Defragmenter.GotAcks || Session.SendQueue.Count > 0 || Session.Fragmenter.GotUnsentFragments )
            {
                do
                {
                    SendAcksAndData();
                } while ( Session.SendQueue.Count > 0 );
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

        private void SendSessionDestroyed()
        {
            SendMessage(
                SSUHeader.MessageTypes.SessionDestroyed,
                Session.MACKey,
                Session.SharedKey,
                ( start, writer ) =>
                {
                    writer.Write8( 0 );

                    return true;
                } );
        }

        private void ResendNotAcked( IEnumerable<DataFragment> fragments )
        {
            bool finished = false;

            var current = fragments.GetEnumerator();

            while ( !finished )
            {
                SendMessage(
                    SSUHeader.MessageTypes.Data,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        var flagbuf = writer.ReadBufLen( 1 );
                        flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.WantReply;

                        // Data
                        var fragcountbuf = writer.ReadBufLen( 1 );
                        int fragmentcount = 0;
                        while( current.MoveNext() )
                        {
                            if ( current.Current.Size > writer.Length ) break;

                            ++fragmentcount;
                            current.Current.WriteTo( writer );
#if LOG_ALL_TRANSPORT
                            DebugUtils.Log( "SSU resending fragment " + current.Current.FragmentNumber.ToString() + 
                                " of message " + current.Current.MessageId.ToString() +
                                ". IsLast: " + current.Current.IsLast.ToString() );
#endif
                            if ( current.Current.SendCount > FragmentedMessage.SendRetriesMTUDecrease ) Session.SendDroppedMessageDetected();
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

            while ( !finished )
            {
                SendMessage(
                    SSUHeader.MessageTypes.Data,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        // Acks
                        var flagbuf = writer.ReadBufLen( 1 );
                        flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.WantReply;
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
#if NO_LOG_ALL_TRANSPORT
                DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": HandleIncomingPeerTestPackage: IP Address not accepted. Ignorning. " + msg.ToString() );
#endif
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
#if NO_LOG_ALL_TRANSPORT
                                    DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Bob, relaying response from Charlie to Alice " + msg.ToString() );
#endif
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
#if NO_LOG_ALL_TRANSPORT
                                    DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Charlie, responding to the final message from Alice " + msg.ToString() );
#endif
                                    msg.WriteTo( writer );

                                    return true;
                                } );
                        }
                        return;

                    default:
                        DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": Unexpedted PeerTest received. " + msg.ToString() );
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
#if NO_LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Charlie, responding to Bob. " + msg.ToString() );
#endif
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
#if NO_LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Charlie, sending first probe to Alice. " + toalice.ToString() );
#endif
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
#if NO_LOG_ALL_TRANSPORT
                DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Bob getting a iniation from Alice, but will drop it due to nonce clash. " + 
                    msg.ToString() );
#endif
                return;
            }

            Session.Host.SetNonceInfo( msg.TestNonce.Peek32( 0 ), PeerTestRole.Bob );

            var pt = new PeerTest( msg.TestNonce,
                Session.RemoteEP.Address, Session.RemoteEP.Port,
                msg.IntroKey );
#if NO_LOG_ALL_TRANSPORT
            DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Bob and sending first relay to Charlie: " + pt.ToString() );
#endif
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
#if NO_LOG_ALL_TRANSPORT
                    DebugUtils.Log( "SSU PeerTest " + Session.DebugId + ": We are Bob, relaying first message to Charlie. " + msg.ToString() );
#endif
                    msg.WriteTo( writer );

                    return true;
                } );
        }
    }
}
