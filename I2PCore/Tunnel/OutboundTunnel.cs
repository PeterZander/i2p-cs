using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Router;
using I2PCore.Data;
using I2PCore.Transport;
using I2PCore.Tunnel.I2NP.Data;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using System.Collections.Concurrent;

namespace I2PCore.Tunnel
{
    public class OutboundTunnel: Tunnel
    {
        protected I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        internal I2PTunnelId SendTunnelId;

        public readonly uint TunnelBuildReplyMessageId = BufUtils.RandomUint();
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

        private bool HandleReceiveQueue()
        {
            while ( true )
            {
                if ( ReceiveQueue.IsEmpty ) return true;

                if ( !ReceiveQueue.TryDequeue( out var msg ) ) continue;

                switch ( msg.MessageType )
                {
                    case I2NPMessage.MessageTypes.VariableTunnelBuildReply:
                        var vtreply = HandleReceivedTunnelBuild( (VariableTunnelBuildReplyMessage)msg.Message );
                        if ( !vtreply ) return false;
                        break;

                    default:
                        Logging.Log( $"OutboundTunnel {TunnelDebugTrace} HandleReceiveQueue: Dropped {msg.MessageType}" );
                        break;
                }
            }
        }

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 5 );
#endif

        private bool HandleReceivedTunnelBuild( VariableTunnelBuildReplyMessage msg )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            var hops = Config.Info.Hops;

            for ( int i = hops.Count - 1; i >= 0; --i )
            {
                var proc = hops[i].ReplyProcessing;
                cipher.Init( false, proc.ReplyKey.Key.ToParametersWithIV( proc.ReplyIV ) );

                for ( int j = 0; j <= i; ++j )
                {
                    cipher.Reset();
                    var pl = msg.ResponseRecords[hops[j].ReplyProcessing.BuildRequestIndex].Payload;
                    cipher.ProcessBytes( pl );
                }
            }

            bool ok = true;
            for ( int i = 0; i < hops.Count; ++i )
            {
                var hop = hops[i];

                var ix = hop.ReplyProcessing.BuildRequestIndex;
                var onerecord = msg.ResponseRecords[ix];

                var okhash = onerecord.CheckHash();
                if ( !okhash )
                {
                    Logging.LogDebug( $"OutboundTunnel {TunnelDebugTrace}: Outbound tunnel build reply, hash check failed from {hop.Peer.IdentHash.Id32Short}" );
                    NetDb.Inst.Statistics.DestinationInformationFaulty( hop.Peer.IdentHash );
                }

                var accept = onerecord.Reply == BuildResponseRecord.RequestResponse.Accept;
                if ( accept )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelMember( hop.Peer.IdentHash );
                }
                else
                {
                    NetDb.Inst.Statistics.DeclinedTunnelMember( hop.Peer.IdentHash );
                }

                ok &= accept && okhash;
                Logging.LogDebug( $"HandleReceivedTunnelBuild: {this}: [{ix}] " +
                    $"from {hop.Peer.IdentHash.Id32Short}. {hops.Count} hops, " +
                    $"Reply: {onerecord.Reply}" );
            }

            if ( ok )
            {
                TunnelProvider.Inst.OutboundTunnelEstablished( this );
                foreach ( var one in hops )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
            }
            else
            {
                foreach ( var one in hops )
                {
                    NetDb.Inst.Statistics.DeclinedTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
                Shutdown();
            }

            return ok;
        }

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
