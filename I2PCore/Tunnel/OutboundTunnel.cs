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

namespace I2PCore.Tunnel
{
    public class OutboundTunnel: Tunnel
    {
        TunnelInfo TunnelSetup;
        
        I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        internal I2PTunnelId SendTunnelId;

        public readonly uint TunnelBuildReplyMessageId = BufUtils.RandomUint();
        public readonly int ReplyTunnelHops;

        public OutboundTunnel( TunnelConfig config, int replytunnelhops ): base( config )
        {
            TunnelSetup = config.Info;

            var outtunnel = TunnelSetup.Hops[0];
            NextHop = outtunnel.Peer.IdentHash;
            SendTunnelId = outtunnel.TunnelId;
            ReplyTunnelHops = replytunnelhops;
        }

        public override int TunnelEstablishmentTimeoutSeconds 
        { 
            get 
            {
                return ( TunnelMemberHops + ReplyTunnelHops ) * 
                    ( Config.Pool == TunnelConfig.TunnelPool.Exploratory 
                        ? ( MeassuredTunnelBuildTimePerHopSeconds * 2 ) / 3
                        : MeassuredTunnelBuildTimePerHopSeconds ); 
            } 
        }

        public override int LifetimeSeconds
        {
            get
            {
                if ( Config.Pool == TunnelConfig.TunnelPool.Exploratory ) 
                    return TunnelLifetimeSeconds / 2;
                return TunnelLifetimeSeconds;
            }
        }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers
        {
            get
            {
                return TunnelSetup.Hops.Select( h => (I2PRouterIdentity)h.Peer );
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
            II2NPHeader msg;

            while ( true )
            {
                lock ( ReceiveQueue )
                {
                    if ( ReceiveQueue.Count == 0 ) return true;
                    msg = ReceiveQueue.Last.Value;
                    ReceiveQueue.RemoveLast();
                }

                switch ( msg.MessageType )
                {
                    case I2NPMessage.MessageTypes.VariableTunnelBuildReply:
                        var vtreply = HandleReceivedTunnelBuild( (VariableTunnelBuildReplyMessage)msg.Message );
                        if ( !vtreply ) return false;
                        break;

                    default:
                        Logging.Log( "OutboundTunnel " + TunnelDebugTrace + " HandleReceiveQueue: Dropped " + msg.MessageType.ToString() );
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

            for ( int i = TunnelSetup.Hops.Count - 1; i >= 0; --i )
            {
                var proc = TunnelSetup.Hops[i].ReplyProcessing;
                cipher.Init( false, proc.ReplyKey.Key.ToParametersWithIV( proc.ReplyIV ) );

                for ( int j = 0; j <= i; ++j )
                {
                    cipher.Reset();
                    var pl = msg.ResponseRecords[TunnelSetup.Hops[j].ReplyProcessing.BuildRequestIndex].Payload;
                    cipher.ProcessBytes( pl );
                }
            }

            bool ok = true;
            for ( int i = 0; i < TunnelSetup.Hops.Count; ++i )
            {
                var hop = TunnelSetup.Hops[i];

                var ix = hop.ReplyProcessing.BuildRequestIndex;
                var onerecord = msg.ResponseRecords[ix];

                var okhash = onerecord.CheckHash();
                if ( !okhash )
                {
                    Logging.Log( "OutboundTunnel " + TunnelDebugTrace + ": Outbound tunnel build reply, hash check failed from " + hop.Peer.IdentHash.Id32Short );
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
                Logging.LogDebug( () => string.Format( "{0}: HandleReceivedTunnelBuild reply[{1}]: from {2}. {3} hops, Tunnel build reply: {4}",
                    this, ix, hop.Peer.IdentHash.Id32Short, TunnelSetup.Hops.Count, onerecord.Reply ) );
            }

            if ( ok )
            {
                TunnelProvider.Inst.OutboundTunnelEstablished( this );
                foreach ( var one in TunnelSetup.Hops )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
            }
            else
            {
                foreach ( var one in TunnelSetup.Hops )
                {
                    NetDb.Inst.Statistics.DeclinedTunnelMember( one.Peer.IdentHash );
                    one.ReplyProcessing = null; // We dont need this anymore
                }
            }

            return ok;
        }

        private bool HandleSendQueue()
        {
            I2NPMessage[] rawdata;

            lock ( SendRawQueue )
            {
                rawdata = SendRawQueue.ToArray();
                SendRawQueue.Clear();
            }
            foreach ( var msg in rawdata )
            {
#if LOG_ALL_TUNNEL_TRANSFER
                if ( FilterMessageTypes.Update( new HashedItemGroup( (int)msg.MessageType, 0x1701 ) ) )
                {
                    Logging.LogDebug( "OutboundTunnel: Send raw  " + NextHop.Id32Short + " : " + msg.ToString() );
                }
#endif
                Bandwidth.DataSent( msg.Payload.Length );
                TransportProvider.Send( NextHop, msg );
            }

            if ( SendQueue.Count == 0 ) return true;

            IEnumerable<TunnelMessage> messages;

            lock ( SendQueue )
            {
                messages = SendQueue.ToArray();
                SendQueue.Clear();
            }

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
                    Logging.LogDebug( "OutboundTunnel: Send " + NextHop.Id32Short + " : " + msg.ToString() );
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

            foreach ( var one in data )
            {
                foreach ( var hop in TunnelSetup.Hops.Reverse<HopInfo>() )
                {
                    one.IV.AesEcbDecrypt( hop.IVKey.Key );
                    cipher.Decrypt( hop.LayerKey.Key, one.IV, one.EncryptedWindow );
                    one.IV.AesEcbDecrypt( hop.IVKey.Key );
                }

                buf.Add( one );
            }

            return buf;
        }

        /*
        public void SendTunnelGateway( I2NPMessage msg )
        {
            lock ( SendQueue )
            {
                SendRawQueue.AddFirst( new TunnelGateway( new I2NPHeader16( msg ), SendTunnelId ) );
            }
        }*/

        public I2NPMessage CreateBuildRequest( InboundTunnel replytunnel )
        {
            var vtb = VariableTunnelBuildMessage.BuildOutboundTunnel( TunnelSetup,
                replytunnel.Destination, replytunnel.GatewayTunnelId,
                TunnelBuildReplyMessageId );

            //Logging.Log( vtb.ToString() );

            return vtb;
        }

        public override string ToString()
        {
            return base.ToString() + " " + Destination.Id32Short;
        }
    }
}
