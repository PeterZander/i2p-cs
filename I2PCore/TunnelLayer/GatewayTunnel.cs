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

namespace I2PCore.TunnelLayer
{
    public class GatewayTunnel: InboundTunnel
    {
        protected readonly I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        public override bool Established { get => true; set => base.Established = value; }

        internal I2PTunnelId SendTunnelId;
        readonly BufLen IVKey;
        readonly BufLen LayerKey;

        internal BandwidthLimiter Limiter;

        PeriodicAction PreTunnelDataBatching = new PeriodicAction( TickSpan.Milliseconds( 500 ) );

        public GatewayTunnel( ITunnelOwner owner, TunnelConfig config, BuildRequestRecord brrec )
            : base( owner, config, 1 )
        {
            Limiter = new BandwidthLimiter( Bandwidth.SendBandwidth, TunnelSettings.GatewayTunnelBitrateLimit );

            ReceiveTunnelId = new I2PTunnelId( brrec.ReceiveTunnel );
            SendTunnelId = new I2PTunnelId( brrec.NextTunnel );

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
            I2NPMessage[] messages = null;

            if ( ReceiveQueue.IsEmpty ) return true;

            var msgs = new List<I2NPMessage>();
            int dropped = 0;
            while ( ReceiveQueue.TryDequeue( out var msg ) )
            {
                if ( Limiter.DropMessage() )
                {
                    ++dropped;
                    continue;
                }

                msgs.Add( msg );
            }
            messages = msgs.ToArray();

#if LOG_ALL_TUNNEL_TRANSFER
            if ( dropped > 0 )
            {
                if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x63e9 ) ) )
                {
                    Logging.LogDebug( $"{this} bandwidth limit. {dropped} dropped messages. {Bandwidth}" );
                }
            }
#endif

            if ( messages == null || messages.Length == 0 ) return true;

            var tdata = TunnelDataMessage.MakeFragments( 
                messages.Select( msg => new TunnelMessageLocal( msg ) )
                , SendTunnelId );

            EncryptTunnelMessages( tdata );

#if LOG_ALL_TUNNEL_TRANSFER
            if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x17f3 ) ) )
            {
                Logging.Log( $"GatewayTunnel {Destination.Id32Short}: TunnelData sent." );
            }
#endif
            foreach ( var tdmsg in tdata )
            {
                TransportProvider.Send( Destination, tdmsg );
                Bandwidth.DataSent( tdmsg.Payload.Length );
                //Logging.LogDebug( $"{this} {Destination.Id32Short}: TDM len {tdmsg.Payload.Length}." );
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
