using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Utils;
using I2PCore.TransportLayer;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.SessionLayer;
using System.Collections.Concurrent;

namespace I2PCore.TunnelLayer
{
    public class EndpointTunnel: InboundTunnel
    {
        protected I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        public override bool Established { get => true; set => base.Established = value; }

        internal I2PTunnelId ResponseTunnelId;
        internal uint ResponseMessageId;

        BufLen IVKey;
        BufLen LayerKey;

        internal BandwidthLimiter Limiter;

        public EndpointTunnel( ITunnelOwner owner, TunnelConfig config, BuildRequestRecord brrec )
            : base( owner, config, 1 )
        {
            Limiter = new BandwidthLimiter( Bandwidth.SendBandwidth, TunnelSettings.EndpointTunnelBitrateLimit );

            ReceiveTunnelId = new I2PTunnelId( brrec.ReceiveTunnel );
            ResponseTunnelId = new I2PTunnelId( brrec.NextTunnel );
            ResponseMessageId = brrec.SendMessageId;

            NextHop = new I2PIdentHash( new BufRefLen( brrec.NextIdent.Hash.Clone() ) );
            IVKey = brrec.IVKey.Clone();
            LayerKey = brrec.LayerKey.Clone();
        }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers
        {
            get
            {
                return Enumerable.Empty<I2PRouterIdentity>();
            }
        }

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 2 );
#endif

        PeriodicAction FragBufferReport = new PeriodicAction( TickSpan.Seconds( 60 ) );

        public override bool Exectue()
        {
            FragBufferReport.Do( delegate()
            {
                var fbsize = Reassembler.BufferedFragmentCount;
                Logging.Log( "EndpointTunnel: " + Destination.Id32Short + " Fragment buffer size: " + fbsize.ToString() );
                if ( fbsize > 2000 ) throw new Exception( "BufferedFragmentCount > 2000 !" ); // Trying to fill my memory?
            } );

            return HandleReceiveQueue();
        }

        private bool HandleReceiveQueue()
        {
            var tdmsgs = new List<TunnelDataMessage>();

            if ( ReceiveQueue.IsEmpty ) return true;

            while ( ReceiveQueue.TryDequeue( out var message ) )
            {
                if ( message.MessageType == I2NPMessage.MessageTypes.TunnelData )
                {
                    // Just drop the non-TunnelData
                    tdmsgs.Add( (TunnelDataMessage)message );
                }
            }

            if ( tdmsgs.Any() )
            {
                return HandleTunnelData( tdmsgs );
            }

            return true;
        }

        TunnelDataFragmentReassembly Reassembler = new TunnelDataFragmentReassembly();

        private bool HandleTunnelData( IEnumerable<TunnelDataMessage> msgs )
        {
            EncryptTunnelMessages( msgs );

            var newmsgs = Reassembler.Process( msgs, out var failed );

            if ( failed )
            {
                Logging.LogWarning( $"{this}: Reassembler failure. Dropping tunnel." );
                Shutdown();
            }

            var dropped = 0;
            foreach ( var one in newmsgs )
            {
                if ( Limiter.DropMessage() )
                {
                    ++dropped;
                    continue;
                }

                try
                {
                    one.Distribute( this );
                }
                catch ( Exception ex )
                {
                    Logging.Log( "EndpointTunnel", ex );
                    throw; // Kill tunnel is strange things happen
                }
            }

#if LOG_ALL_TUNNEL_TRANSFER
            if ( dropped > 0 )
            {
                Logging.LogDebug( () => string.Format( "{0} bandwidth limit. {1} dropped messages. {2}", this, dropped, Bandwidth ) );
            }
#endif

            return true;
        }

        private void EncryptTunnelMessages( IEnumerable<TunnelDataMessage> msgs )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            foreach ( var msg in msgs )
            {
                msg.IV.AesEcbEncrypt( IVKey.ToByteArray() );

                cipher.Init( true, LayerKey.ToParametersWithIV( msg.IV ) );
                cipher.ProcessBytes( msg.EncryptedWindow );

                msg.IV.AesEcbEncrypt( IVKey.ToByteArray() );

                // The 0 should be visible now
                msg.UpdateFirstDeliveryInstructionPosition();
            }
        }
    }
}
