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
using I2PCore.Router;

namespace I2PCore.Tunnel
{
    public class GatewayTunnel: Tunnel
    {
        I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        internal I2PTunnelId SendTunnelId;

        BufLen IVKey;
        BufLen LayerKey;

        internal BandwidthLimiter Limiter;

        PeriodicAction PreTunnelDataBatching = new PeriodicAction( TickSpan.Milliseconds( 3000 ) );

        public GatewayTunnel( BuildRequestRecord brrec )
            : base( null )
        {
            Config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Outbound,
                TunnelConfig.TunnelPool.External,
                null );

            Limiter = new BandwidthLimiter( Bandwidth.SendBandwidth, TunnelSettings.GatewayTunnelBitrateLimit );

            ReceiveTunnelId = new I2PTunnelId( brrec.ReceiveTunnel );
            SendTunnelId = new I2PTunnelId( brrec.NextTunnel );

            NextHop = new I2PIdentHash( new BufRefLen( brrec.NextIdent.Hash.Clone() ) );
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
            var ok = true;

            PreTunnelDataBatching.Do( () => { ok = HandleReceiveQueue(); } );

            return ok;
        }

        private bool HandleSendQueue()
        {
            return false;
        }

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 2 );
#endif

        private bool HandleReceiveQueue()
        {
            II2NPHeader16[] messages = null;

            lock ( ReceiveQueue )
            {
                if ( ReceiveQueue.Count == 0 ) return true;

                var msgs = new List<II2NPHeader16>();
                int dropped = 0;
                foreach ( var msg in ReceiveQueue )
                {
                    if ( Limiter.DropMessage() )
                    {
                        ++dropped;
                        continue;
                    }

                    msgs.Add( (II2NPHeader16)msg );
                }
                messages = msgs.ToArray();

#if LOG_ALL_TUNNEL_TRANSFER
                if ( dropped > 0 )
                {
                    if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x63e9 ) ) )
                    {
                        Logging.LogDebug( () => string.Format( "{0} bandwidth limit. {1} dropped messages. {2}", this, dropped, Bandwidth ) );
                    }
                }
#endif
            }

            if ( messages == null || messages.Length == 0 ) return true;

            var tdata = TunnelDataMessage.MakeFragments( messages.Select( msg => (TunnelMessage)( new TunnelMessageLocal( msg ) ) ), SendTunnelId );

            EncryptTunnelMessages( tdata );

#if LOG_ALL_TUNNEL_TRANSFER
            if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x17f3 ) ) )
            {
                Logging.Log( "GatewayTunnel " + Destination.Id32Short + ": TunnelData sent." );
            }
#endif
            foreach ( var tdmsg in tdata )
            {
                Bandwidth.DataSent( tdmsg.Payload.Length ); 
                TransportProvider.Send( Destination, tdmsg );
            }
            return true;
        }

        private void EncryptTunnelMessages( IEnumerable<TunnelDataMessage> msgs )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            foreach ( var msg in msgs )
            {
                msg.IV.AesEcbEncrypt( IVKey );
                cipher.Encrypt( LayerKey, msg.IV, msg.EncryptedWindow );
                msg.IV.AesEcbEncrypt( IVKey );
            }
        }
    }
}
