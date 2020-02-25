using System.Collections.Generic;
using System.Linq;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.TransportLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using System.Collections.Concurrent;

namespace I2PCore.TunnelLayer
{
    public class OutboundTunnel: Tunnel
    {
        protected I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        internal I2PTunnelId SendTunnelId;

        public readonly uint TunnelBuildReplyMessageId = I2NPMessage.GenerateMessageId();
        public readonly int ReplyTunnelHops;

        public OutboundTunnel( ITunnelOwner owner, TunnelConfig config, int replytunnelhops )
            : base( owner, config )
        {
            var outtunnel = config.Info.Hops[0];
            NextHop = outtunnel.Peer.IdentHash;
            SendTunnelId = outtunnel.TunnelId;
            ReplyTunnelHops = replytunnelhops;
        }

        public override TickSpan TunnelEstablishmentTimeout 
        { 
            get 
            {
                var hops = ReplyTunnelHops + TunnelMemberHops;
                var timeperhop = Config.Pool == TunnelConfig.TunnelPool.Exploratory
                        ? ( MeassuredTunnelBuildTimePerHop * 2 ) / 3
                        : MeassuredTunnelBuildTimePerHop;

                return timeperhop * hops;
            } 
        }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers
        {
            get
            {
                return Config.Info.Hops.Select( h => (I2PRouterIdentity)h.Peer );
            }
        }

        public override bool Exectue()
        {
            if ( Terminated ) return false;

            if ( NextHop == null )
            {
                Logging.LogDebug( "OutboundTunnel: NextHop == null. Fail." );
                return false;
            }

            return HandleReceiveQueue() && HandleSendQueue();
        }

        protected ConcurrentQueue<TunnelMessage> SendQueue = new ConcurrentQueue<TunnelMessage>();

        public virtual void Send( TunnelMessage msg )
        {
            SendQueue.Enqueue( msg );
        }

        private bool HandleReceiveQueue()
        {
            while ( true )
            {
                if ( ReceiveQueue.IsEmpty ) return true;
                if ( !ReceiveQueue.TryDequeue( out var msg ) ) continue;

                switch ( msg.MessageType )
                {
                    default:
                        Logging.Log( $"OutboundTunnel {TunnelDebugTrace} HandleReceiveQueue: Dropped {msg.MessageType}" );
                        break;
                }
            }
        }

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 5 );
#endif

        private bool HandleSendQueue()
        {
            if ( SendQueue.IsEmpty ) return true;

            IEnumerable<TunnelMessage> messages;

            messages = SendQueue.ToArray();
            SendQueue = new ConcurrentQueue<TunnelMessage>();

            return CreateTunnelMessageFragments( messages );
        }

        private bool CreateTunnelMessageFragments( IEnumerable<TunnelMessage> messages )
        {
            var data = TunnelDataMessage.MakeFragments( messages, SendTunnelId );

            var encr = OutboundGatewayDecrypt( data );
            foreach ( var msg in encr )
            {
#if LOG_ALL_TUNNEL_TRANSFER
                if ( FilterMessageTypes.Update( new HashedItemGroup( (int)msg.MessageType, 0x4272 ) ) )
                {
                    Logging.LogDebug( $"OutboundTunnel: Send {NextHop.Id32Short} : {msg}" );
                }
#endif
                Bandwidth.DataSent( msg.Payload.Length );
                TransportProvider.Send( NextHop, msg );
            }

            return true;
        }

        internal IEnumerable<I2NPMessage> OutboundGatewayDecrypt( IEnumerable<TunnelDataMessage> data )
        {
            var buf = new List<I2NPMessage>();
            var cipher = new CbcBlockCipher( new AesEngine() );

            var hopsreverse = Config.Info.Hops.Reverse<HopInfo>();

            foreach ( var one in data )
            {
                foreach ( var hop in hopsreverse )
                {
                    one.IV.AesEcbDecrypt( hop.IVKey.Key );
                    cipher.Decrypt( hop.LayerKey.Key, one.IV, one.EncryptedWindow );
                    one.IV.AesEcbDecrypt( hop.IVKey.Key );
                }

                buf.Add( one );
            }

            return buf;
        }

        public I2NPMessage CreateBuildRequest( InboundTunnel replytunnel )
        {
            var vtb = VariableTunnelBuildMessage.BuildOutboundTunnel( Config.Info,
                replytunnel.Destination, replytunnel.GatewayTunnelId,
                TunnelBuildReplyMessageId );

            //Logging.Log( vtb.ToString() );

            return vtb;
        }

        public override string ToString()
        {
            return $"{base.ToString()} {Destination.Id32Short}";
        }
    }
}
