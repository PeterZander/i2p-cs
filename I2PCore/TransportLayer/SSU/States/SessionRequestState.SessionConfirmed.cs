using System;
using System.Collections.Generic;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SessionRequestState: SSUState
    {
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
                    writer.Write( BufUtils.RandomBytes( padding ) );

                    var baddr = new BufLen( Session.UnwrappedRemoteAddress.GetAddressBytes() );
                    var bport = BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port );
#if LOG_MUCH_TRANSPORT
                    Logging.LogTransport( $"SSU {this}: X for signature {Request.X.Key}." );
                    Logging.LogTransport( $"SSU {this}: Y for signature {Request.Y.Key}." );
                    Logging.LogTransport( $"SSU {this}: Alice address for signature {Request.SCMessage.Address}. Port {Request.SCMessage.Port}." );
                    Logging.LogTransport( $"SSU {this}: Bob address for signature {baddr}. Port {bport}." );
                    Logging.LogTransport( $"SSU {this}: Relay tag {Request.SCMessage.RelayTag}. Signon time {Session.SignOnTimeA}." );
#endif

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                            X.Key, Y.Key, 
                            SCMessage.Address, SCMessage.Port,
                            baddr, bport, 
                            SCMessage.RelayTag, BufUtils.To32BL( Session.SignOnTimeA )
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
                    writer.Write( BufUtils.RandomBytes( padding ) );

                    var baddr = new BufLen( Session.UnwrappedRemoteAddress.GetAddressBytes() );

                    var sign = I2PSignature.DoSign( Session.MyRouterContext.PrivateSigningKey,
                            X.Key, Y.Key, 
                            SCMessage.Address, SCMessage.Port,
                            baddr, BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port ), 
                            SCMessage.RelayTag, BufUtils.To32BL( Session.SignOnTimeA )
                       );
                    writer.Write( sign );

                    Logging.LogTransport( $"SSU {this}: {Session.RemoteEP} " +
                        $"sending fragment {frag + 1} of {datafragments.Count + 1}, [0x{writer - start - SSUHeader.FIXED_HEADER_SIZE:X}] bytes." );

                    return true;
                } );
        }

        protected SSUState SendConnectionEstablished()
        {
            Logging.LogTransport( $"SSU {this}: Sending SessionConfirmed message." );

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

            Logging.LogTransport( $"SSU SessionRequestState: Session {Session.DebugId} established. " + 
                $"Moving to Established state." );

            Session.Host.ReportConnectionCreated( Session, Session.RemoteRouterIdentity.IdentHash );
            Session.ReportConnectionEstablished();

            return new EstablishedState( Session );
        }
    }
}
