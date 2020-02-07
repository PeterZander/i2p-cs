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
    public class SessionConfirmedState: SSUState
    {
        SessionRequestState Request;

        public SessionConfirmedState( SSUSession sess, SessionRequestState req )
            : base( sess )
        {
            Request = req;
        }

        PeriodicAction ResendSessionConfirmedAction = new PeriodicAction( TickSpan.Seconds( HandshakeStateTimeoutSeconds / 5 ), true );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeoutSeconds ) )
            {
                Session.Host.EPStatisitcs.ConnectionTimeout( Session.RemoteEP );
                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                throw new FailedToConnectException( "SSU SessionConfirmedState " + Session.DebugId + " Failed to connect. Timeout." );
            }

            ResendSessionConfirmedAction.Do( () =>
            {

                if ( ++Retries > HandshakeStateMaxRetries ) 
                    throw new FailedToConnectException( "SSU " + Session.DebugId + " Failed to connect" );

                Logging.LogTransport( "SSU SessionConfirmedState " + Session.DebugId + " : Resending SessionConfirmed message." );

                // SendFragmentedSessionConfirmed(); // Not all routers seem to support this
                /**
                 * From InboundEstablishState.java
                 * 
                 -----8<-----
                 *  Note that while a SessionConfirmed could in theory be fragmented,
                 *  in practice a RouterIdentity is 387 bytes and a single fragment is 512 bytes max,
                 *  so it will never be fragmented.
                 -----8<-----
                 */

                SendUnfragmentedSessionConfirmed();
            } );

            return this;
        }

        private void SendUnfragmentedSessionConfirmed()
        {
            var ri = new BufLen( Session.MyRouterContext.MyRouterInfo.ToByteArray() );

            SendMessage(
                SSUHeader.MessageTypes.SessionConfirmed,
                Session.MACKey,
                Session.SharedKey,
                ( start, writer ) =>
                {
                    writer.Write8( (byte)( ( 0 << 4 ) + 1 ) );
                    writer.WriteFlip16( (ushort)ri.Length );
                    writer.Write( ri );

                    Session.SignOnTimeA = BufUtils.Flip32( SSUHost.SSUTime( DateTime.UtcNow ) );
                    writer.Write32( Session.SignOnTimeA );
                    var padding = BufUtils.Get16BytePadding( Session.MyRouterContext.Certificate.SignatureLength + ( writer - start ) );
                    writer.Write( BufUtils.Random( padding ) );

                    var baddr = new BufLen( Session.RemoteEP.Address.GetAddressBytes() );
                    var bport = BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port );
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( string.Format( "SSU SessionConfirmedState {0}: X for signature {1}.",
                        Session.DebugId, Request.X.Key ) );
                    Logging.LogTransport( string.Format( "SSU SessionConfirmedState {0}: Y for signature {1}.",
                        Session.DebugId, Request.Y.Key ) );
                    Logging.LogTransport( string.Format( "SSU SessionConfirmedState {0}: Alice address for signature {1}. Port {2}.",
                        Session.DebugId, Request.SCMessage.Address, Request.SCMessage.Port ) );
                    Logging.LogTransport( string.Format( "SSU SessionConfirmedState {0}: Bob address for signature {1}. Port {2}.",
                        Session.DebugId, baddr, bport ) );
                    Logging.LogTransport( string.Format( "SSU SessionConfirmedState {0}: Relay tag {1}. Signon time {2}.",
                        Session.DebugId, Request.SCMessage.RelayTag, (BufLen)Session.SignOnTimeA ) );
#endif

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                            Request.X.Key, Request.Y.Key, 
                            Request.SCMessage.Address, Request.SCMessage.Port,
                            baddr, bport, 
                            Request.SCMessage.RelayTag, (BufLen)Session.SignOnTimeA
                        );
                    writer.Write( sign );

#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( string.Format( "SessionConfirmedState {0}: sending unfragmented SessionConfirmed. {1} bytes [0x{1:X}].",
                        Session.DebugId,
                        writer - start - SSUHeader.FIXED_HEADER_SIZE ) );
#endif

                    return true;
                } );
        }

        private void SendFragmentedSessionConfirmed()
        {
            var ri = new BufLen( Session.MyRouterContext.MyRouterInfo.ToByteArray() );
            var rireader = new BufRefLen( ri );

            var datafragments = new List<BufLen>();
            while ( rireader.Length > 0 )
            {
                datafragments.Add( rireader.ReadBufLen( Math.Min( rireader.Length, 472 ) ) );
            }

            for ( int i = 0; i < datafragments.Count; ++i )
            {
#if LOG_ALL_TRANSPORT
                Logging.LogTransport( string.Format( "SessionConfirmedState {0}: sending fragment {1} of {2}, {3} bytes [0x{3:X}].",
                    Session.DebugId,
                    i + 1,
                    datafragments.Count + 1,
                    datafragments[i].Length ) );
#endif
                SendMessage(
                    SSUHeader.MessageTypes.SessionConfirmed,
                    Session.MACKey,
                    Session.SharedKey,
                    ( start, writer ) =>
                    {
                        writer.Write8( (byte)( ( i << 4 ) + datafragments.Count + 1 ) );
                        writer.WriteFlip16( (ushort)datafragments[i].Length );
                        writer.Write( datafragments[i] );

                        return true;
                    } );
            }

            SendMessage(
                SSUHeader.MessageTypes.SessionConfirmed,
                Session.MACKey,
                Session.SharedKey,
                ( start, writer ) =>
                {
                    var frag = datafragments.Count;
                    writer.Write8( (byte)( ( frag << 4 ) + frag + 1 ) );
                    writer.WriteFlip16( 0 );

                    Session.SignOnTimeA = BufUtils.Flip32( SSUHost.SSUTime( DateTime.UtcNow ) );
                    writer.Write32( Session.SignOnTimeA );
                    var padding = BufUtils.Get16BytePadding( Session.MyRouterContext.Certificate.SignatureLength + ( writer - start ) );
                    writer.Write( BufUtils.Random( padding ) );

                    var baddr = new BufLen( Session.RemoteEP.Address.GetAddressBytes() );

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                            Request.X.Key, Request.Y.Key, 
                            Request.SCMessage.Address, Request.SCMessage.Port,
                            baddr, BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port ), 
                            Request.SCMessage.RelayTag, (BufLen)Session.SignOnTimeA
                       );
                    writer.Write( sign );

#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( string.Format( "SessionConfirmedState {0}: sending fragment {1} of {2}, {3} bytes [0x{3:X}].",
                        Session.DebugId,
                        frag + 1,
                        datafragments.Count + 1,
                        writer - start - SSUHeader.FIXED_HEADER_SIZE ) );
#endif

                    return true;
                } );
        }

        protected override BufLen CurrentMACKey { get { return Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            if ( header.MessageType == SSUHeader.MessageTypes.SessionCreated )
            {
#if LOG_ALL_TRANSPORT
                Logging.LogTransport( "SSU SessionConfirmedState " + Session.DebugId + ": Unexpected message received: " + header.MessageType.ToString() );
#endif
                return this;
            }

            Logging.LogTransport( "SSU SessionConfirmedState: Session " + Session.DebugId + " established. " + 
                header.MessageType.ToString() + " received. Moving to Established state." );
            var next = new EstablishedState( Session );
            Session.ReportConnectionEstablished();

            return next.HandleMessage( header, reader );
        }
    }
}
