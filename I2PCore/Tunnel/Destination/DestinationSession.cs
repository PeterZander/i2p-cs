using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel
{
    public delegate InboundTunnel InboundTunnelSelector();

    public class DestinationSession
    {
        internal static readonly TickSpan LeaseSetUpdateMaxAge = TickSpan.Seconds( 45 );

        public readonly I2PKeysAndCert Destination;

        I2PSessionKey SessionKey = new I2PSessionKey();
        List<I2PSessionTag> SessionTags = new List<I2PSessionTag>();
        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );
        TagsTransferWindow TagsValiditySlidingWindow;
        InboundTunnelSelector SelInboundTunnel;

        internal I2PLeaseSet LatestLocalLeaseSet = null;
        TickCounter LatestLeaseSetSendTime = TickCounter.MaxDelta;

        internal I2PLeaseSet LatestRemoteLeaseSet = null;

        uint LatestEGAckMessageId = 0;

        public DestinationSession( I2PKeysAndCert dest, OutboundTunnelSelector tunnelsel, InboundTunnelSelector intunnelsel )
        {
            TagsValiditySlidingWindow = new TagsTransferWindow( this, tunnelsel );
            Destination = dest;
            SelInboundTunnel = intunnelsel;
        }

        public void Reset()
        {
            SessionKey = new I2PSessionKey();
            lock ( SessionTags ) SessionTags.Clear();
        }

        public GarlicCreationInfo Encrypt( bool explack, uint trackingid, params GarlicCloveDelivery[] cloves )
        {
            lock ( SessionTags )
            {
                SessionTags.RemoveAll( t => t.Created.DeltaToNow.ToMinutes > ( I2PSessionTag.TagLifetimeMinutes - 1 ) );
            }

            if ( SessionTags.Count == 0 ) return GenerateNewSessionTags( trackingid, cloves );
            return UseExistingSessionTags( explack, trackingid, cloves );
        }

        internal GarlicCreationInfo Send( bool explack, params GarlicCloveDelivery[] cloves )
        {
            return TagsValiditySlidingWindow.Send( explack, cloves );
        }

        GarlicCreationInfo GenerateNewSessionTags( uint trackingid, GarlicCloveDelivery[] cloves )
        {
            var newtags = new List<I2PSessionTag>();
            for ( int i = 0; i < 50; ++i ) newtags.Add( new I2PSessionTag() );

            lock( SessionTags )
            {
                SessionTags.AddRange( newtags );
            }

            // Add a ACK message
            DeliveryStatusMessage ackmsg;
            var msg = AddExplAck( cloves, out ackmsg );

            var payload = msg.ToByteArray();
            var dest = new BufLen( new byte[61000] );
            var writer = new BufRefLen( dest, 4 ); // Reserve 4 bytes for GarlicMessageLength

            // ElGamal block
            var egbuf = new BufLen( writer, 0, 222 );
            var sessionkeybuf = new BufLen( egbuf, 0, 32 );
            var preivbuf = new BufLen( egbuf, 32, 32 );
            var egpadding = new BufLen( egbuf, 64 );

            sessionkeybuf.Poke( SessionKey.Key, 0 );
            preivbuf.Randomize();
            egpadding.Randomize();

            var preiv = preivbuf.Clone();

            var eg = new ElGamalCrypto( Destination.PublicKey );
            eg.Encrypt( writer, egbuf, true );

            // AES block
            var aesstart = new BufLen( writer );
            var aesblock = new GarlicAESBlock( writer, newtags, null, new BufRefLen( payload ) );

            var pivh = I2PHashSHA256.GetHash( preiv );

            Cipher.Init( true, SessionKey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
            Cipher.ProcessBytes( aesblock.DataBuf );

            var length = writer - dest;
            dest.PokeFlip32( (uint)( length - 4 ), 0 );

            LatestEGAckMessageId = ackmsg.MessageId;

#if LOG_ALL_TUNNEL_TRANSFER
            DebugUtils.LogDebug( () => string.Format(
                "DestinationSession: Garlic generated with ElGamal encryption, {0} cloves. {1} tags available. Ack MessageId: {2}.",
                msg.Cloves.Count, SessionTags.Count, LatestEGAckMessageId ) );
#endif

            return new GarlicCreationInfo( 
                Destination.IdentHash,
                cloves,
                new EGGarlic( new BufRefLen( dest, 0, length ) ), 
                GarlicCreationInfo.KeyUsed.ElGamal,
                SessionTags.Count(),
                trackingid,
                ackmsg.MessageId,
                LatestEGAckMessageId );
        }

        private Garlic AddExplAck( GarlicCloveDelivery[] cloves, out DeliveryStatusMessage ackmsg )
        {
            var replytunnel = SelInboundTunnel();
            if ( replytunnel == null ) throw new FailedToConnectException( "No inbound tunnels available" );
            ackmsg = new DeliveryStatusMessage( I2NPHeader.GenerateMessageId() );
            var ackclove = new GarlicCloveDeliveryTunnel( ackmsg, replytunnel );
            var exp = new I2PDate( DateTime.UtcNow.AddMinutes( 5 ) );
            var msg = new Garlic( cloves.Concat( new GarlicCloveDelivery[] { ackclove } ).Select( d => new GarlicClove( d, exp ) ) );

#if LOG_ALL_TUNNEL_TRANSFER
            var ackmsgid = ackmsg.MessageId;
            DebugUtils.LogDebug( () => string.Format(
                "DestinationSession: Added ACK message with MessageId: {0} to {1} cloves. Dest: {2}: {3}",
                ackmsgid, cloves.Length, replytunnel.Destination.Id32Short, replytunnel.ReceiveTunnelId ) );
#endif

            return msg;
        }

        GarlicCreationInfo UseExistingSessionTags( bool explack, uint trackingid, GarlicCloveDelivery[] cloves )
        {
            Garlic msg;
            DeliveryStatusMessage ackmsg = null;

            if ( explack )
            {
                msg = AddExplAck( cloves, out ackmsg );
            }
            else
            {
                var exp = new I2PDate( DateTime.UtcNow.AddMinutes( 5 ) );
                msg = new Garlic( cloves.Select( d => new GarlicClove( d, exp ) ).ToArray() );
            }

#if LOG_ALL_TUNNEL_TRANSFER
            DebugUtils.LogDebug( () => string.Format(
                "DestinationSession: Garlic generated with {0} cloves. {1} tags available.",
                msg.Cloves.Count, SessionTags.Count ) );
#endif

            var payload = msg.ToByteArray();
            var dest = new BufLen( new byte[61000] );
            var writer = new BufRefLen( dest, 4 ); // Reserve 4 bytes for GarlicMessageLength

            I2PSessionTag tag;
            lock ( SessionTags )
            {
                var ix = BufUtils.RandomInt( SessionTags.Count );
                tag = SessionTags[ix];
                SessionTags.RemoveAt( ix );
            }

            // Tag as header
            writer.Write( tag.Value );

            // AES block
            var aesstart = new BufLen( writer );
            var aesblock = new GarlicAESBlock( writer, null, null, new BufRefLen( payload ) );

            var pivh = I2PHashSHA256.GetHash( tag.Value );

            Cipher.Init( true, SessionKey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
            Cipher.ProcessBytes( aesblock.DataBuf );

            var length = writer - dest;
            dest.PokeFlip32( (uint)( length - 4 ), 0 );

            return new GarlicCreationInfo(
                Destination.IdentHash,
                cloves,
                new EGGarlic( new BufRefLen( dest, 0, length ) ),
                GarlicCreationInfo.KeyUsed.Aes,
                SessionTags.Count(),
                trackingid,
                explack ? (uint?)ackmsg.MessageId : null,
                LatestEGAckMessageId );
        }

        internal void Run()
        {
            if ( LatestLocalLeaseSet != null && LatestLeaseSetSendTime.DeltaToNow > LeaseSetUpdateMaxAge )
            {
                LocalLeaseSetUpdated( LatestLocalLeaseSet );
            }

            TagsValiditySlidingWindow.Run();
        }

        internal void LocalLeaseSetUpdated( I2PLeaseSet leaseset )
        {
            LatestLocalLeaseSet = leaseset;
            var dbsmessage = new DatabaseStoreMessage( leaseset );
            var info = Send( true, new GarlicCloveDeliveryDestination( dbsmessage, Destination.IdentHash ) );

#if LOG_ALL_TUNNEL_TRANSFER
            if ( info != null )
            {
	            DebugUtils.LogDebug( () => string.Format(
	                "DestinationSession: LeaseSet update bundled in Destination trafic. ({0}) TrackingId: {1}, Ack MessageId: {2}.",
	                info.KeyType, info.TrackingId, info.AckMessageId ) );
            }
#endif

            LatestLeaseSetSendTime.SetNow();
        }

        internal void RemoteLeaseSetUpdated( I2PLeaseSet leaseset )
        {
            LatestRemoteLeaseSet = leaseset;

#if LOG_ALL_TUNNEL_TRANSFER
            DebugUtils.LogDebug( () => string.Format(
                "DestinationSession: LeaseSet updated for {0}.", leaseset.Destination.IdentHash.Id32Short ) );
#endif
        }

        internal bool RemoteLeaseSetKnown()
        {
            return LatestRemoteLeaseSet != null;
        }
    }
}
