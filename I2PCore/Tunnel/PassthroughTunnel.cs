using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Utils;
using I2PCore.Transport;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace I2PCore.Tunnel
{
    public class PassthroughTunnel: Tunnel
    {
        public override I2PIdentHash Destination { get { return NextHop; } }
        I2PIdentHash NextHop;

        internal I2PTunnelId SendTunnelId;

        BufLen IVKey;
        BufLen LayerKey;

        internal BandwidthLimiter Limiter;

        public PassthroughTunnel( BuildRequestRecord brrec )
            : base( null )
        {
            Config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.External,
                null );

            Limiter = new BandwidthLimiter( Bandwidth.SendBandwidth, TunnelSettings.PassthroughTunnelBitrateLimit );

            ReceiveTunnelId = new I2PTunnelId( brrec.ReceiveTunnel );
            NextHop = new I2PIdentHash( new BufRefLen( brrec.NextIdent.Hash.Clone() ) );
            SendTunnelId = new I2PTunnelId( brrec.NextTunnel );

            IVKey = brrec.IVKey.Clone();
            LayerKey = brrec.LayerKey.Clone();
        }

        public override int TunnelEstablishmentTimeoutSeconds { get { return 20; } }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers
        {
            get
            {
                return null;
            }
        }

        public override bool Exectue()
        {
            return HandleReceiveQueue();
        }

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 2 );
#endif

        private bool HandleReceiveQueue()
        {
            TunnelDataMessage[] tdmsgs = null;

            lock ( ReceiveQueue )
            {
                if ( ReceiveQueue.Count == 0 ) return true;

                if ( ReceiveQueue.Any( mq => mq.MessageType == I2NPMessage.MessageTypes.TunnelData ) )
                {
                    var removelist = ReceiveQueue.Where( mq => mq.MessageType == I2NPMessage.MessageTypes.TunnelData );
                    tdmsgs = removelist.Select( mq => (TunnelDataMessage)mq.Message ).ToArray();
                }

                if ( ReceiveQueue.Count > 0 )
                {
                    ReceiveQueue.Clear(); // Just drop the non-TunnelData
                }
            }

            if ( tdmsgs != null )
            {
                return HandleTunnelData( tdmsgs );
            }

            return true;
        }

#if LOG_ALL_TUNNEL_TRANSFER
        PeriodicLogger LogDataSent = new PeriodicLogger( 15 );
#endif

        private bool HandleTunnelData( IEnumerable<TunnelDataMessage> msgs )
        {
            EncryptTunnelMessages( msgs );

#if LOG_ALL_TUNNEL_TRANSFER
            LogDataSent.Log( () => "PassthroughTunnel " + Destination.Id32Short + " TunnelData sent." );
#endif
            var dropped = 0;
            foreach ( var one in msgs )
            {
                if ( Limiter.DropMessage() )
                {
                    ++dropped;
                    continue;
                }

                one.TunnelId = SendTunnelId;
                Bandwidth.DataSent( one.Payload.Length );
                TransportProvider.Send( Destination, one );
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
            }
        }
    }
}
