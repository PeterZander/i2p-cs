using System;
using System.Collections.Generic;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.TransportLayer.SSU
{
    public class SessionConfirmedState: SSUState
    {
        SessionRequestState Request;

        public SessionConfirmedState( SSUSession sess, SessionRequestState req )
            : base( sess )
        {
            Request = req;
        }

        //PeriodicAction ResendSessionConfirmedAction = new PeriodicAction( HandshakeStateTimeout / 5, true );

        public override SSUState Run()
        {
            SendUnfragmentedSessionConfirmed();
            SendUnfragmentedSessionConfirmed();

            Session.ReportConnectionEstablished();

            return new EstablishedState( Session );

            /*
            if ( Timeout( HandshakeStateTimeout ) )
            {
                Session.Host.EPStatisitcs.ConnectionTimeout( Session.RemoteEP );
                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                throw new FailedToConnectException( $"SSU {this}: Failed to connect. Timeout." );
            }

            ResendSessionConfirmedAction.Do( () =>
            {

                if ( ++Retries > HandshakeStateMaxRetries ) 
                    throw new FailedToConnectException( $"SSU {Session.DebugId} Failed to connect" );

                Logging.LogTransport( $"SSU {this}: Resending SessionConfirmed message." );

                // SendFragmentedSessionConfirmed(); // Not all routers seem to support this
                /**
                 * From InboundEstablishState.java
                 * 
                 -----8<-----
                 *  Note that while a SessionConfirmed could in theory be fragmented,
                 *  in practice a RouterIdentity is 387 bytes and a single fragment is 512 bytes max,
                 *  so it will never be fragmented.
                 -----8<-----
                 *

                SendUnfragmentedSessionConfirmed();
            } );

            return this;
            */
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
#if LOG_MUCH_TRANSPORT
                    Logging.LogTransport( $"SSU {this}: X for signature {Request.X.Key}." );
                    Logging.LogTransport( $"SSU {this}: Y for signature {Request.Y.Key}." );
                    Logging.LogTransport( $"SSU {this}: Alice address for signature {Request.SCMessage.Address}. Port {Request.SCMessage.Port}." );
                    Logging.LogTransport( $"SSU {this}: Bob address for signature {baddr}. Port {bport}." );
                    Logging.LogTransport( $"SSU {this}: Relay tag {Request.SCMessage.RelayTag}. Signon time {(BufLen)Session.SignOnTimeA}." );
#endif

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                            Request.X.Key, Request.Y.Key, 
                            Request.SCMessage.Address, Request.SCMessage.Port,
                            baddr, bport, 
                            Request.SCMessage.RelayTag, (BufLen)Session.SignOnTimeA
                        );
                    writer.Write( sign );

                    Logging.LogTransport( $"SSU {this}: {Session.RemoteEP} " +
                        $"sending unfragmented SessionConfirmed [0x{writer - start - SSUHeader.FIXED_HEADER_SIZE:X}] bytes." );
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
                Logging.LogTransport( $"SSU {this}: {Session.RemoteEP} " +
                    $"sending fragment {i + 1} of {datafragments.Count + 1}, [0x{datafragments[i].Length:X}] bytes." );

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

                    Logging.LogTransport( $"SSU {this}: {Session.RemoteEP} " +
                        $"sending fragment {frag + 1} of {datafragments.Count + 1}, [0x{writer - start - SSUHeader.FIXED_HEADER_SIZE:X}] bytes." );

                    return true;
                } );
        }

        protected override BufLen CurrentMACKey { get { return Session.MACKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.SharedKey; } }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            if ( header.MessageType == SSUHeader.MessageTypes.SessionCreated )
            {
                Logging.LogTransport( $"SSU SessionConfirmedState {Session.DebugId}: Unexpected message received: {header.MessageType}" );
                return this;
            }

            Logging.LogTransport( $"SSU SessionConfirmedState: Session {Session.DebugId} established. " + 
                $"{header.MessageType} received. Moving to Established state." );

            var next = new EstablishedState( Session );
            Session.ReportConnectionEstablished();

            return next.HandleMessage( header, reader );
        }
    }
}
