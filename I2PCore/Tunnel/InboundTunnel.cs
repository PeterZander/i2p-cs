using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Transport;
using System.Threading;

namespace I2PCore.Tunnel
{
    public class InboundTunnel: Tunnel
    {
        I2PIdentHash RemoteGateway;
        public override I2PIdentHash Destination { get { return RemoteGateway; } }

        internal I2PTunnelId GatewayTunnelId;

        internal bool Fake0HopTunnel;
        internal TunnelInfo TunnelSetup;

        public readonly uint TunnelBuildReplyMessageId = BufUtils.RandomUint();
        public readonly int OutTunnelHops;

        static readonly object DeliveryStatusReceivedLock = new object();
        public static event Action<DeliveryStatusMessage> DeliveryStatusReceived;

        readonly object GarlicMessageReceivedLock = new object();
        public event Action<GarlicMessage> GarlicMessageReceived;

        public InboundTunnel( TunnelConfig config, int outtunnelhops ): base( config )
        {
            if ( config != null )
            {
                Fake0HopTunnel = false;
                TunnelSetup = config.Info;
                OutTunnelHops = outtunnelhops;

                var gw = TunnelSetup.Hops[0];
                RemoteGateway = gw.Peer.IdentHash;
                GatewayTunnelId = gw.TunnelId;

                ReceiveTunnelId = TunnelSetup.Hops.Last().TunnelId;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"InboundTunnel: Tunnel {Destination.Id32Short} created." );
#endif
            }
            else
            {
                Fake0HopTunnel = true;

                var hops = new List<HopInfo>
                {
                    new HopInfo( RouterContext.Inst.MyRouterIdentity, new I2PTunnelId() )
                };
                TunnelSetup = new TunnelInfo( hops );

                Config = new TunnelConfig(
                    TunnelConfig.TunnelDirection.Inbound,
                    TunnelConfig.TunnelPool.Exploratory,
                    TunnelSetup );

                ReceiveTunnelId = TunnelSetup.Hops.Last().TunnelId;
                RemoteGateway = RouterContext.Inst.MyRouterIdentity.IdentHash;
                GatewayTunnelId = ReceiveTunnelId;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"InboundTunnel {TunnelDebugTrace}: 0-hop tunnel {Destination.Id32Short} created." );
#endif
            }
        }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers 
        {
            get
            {
                if ( TunnelSetup == null ) return null; 
                return TunnelSetup.Hops.Select( h => (I2PRouterIdentity)h.Peer );
            }
        }

        public override int TunnelEstablishmentTimeoutSeconds 
        { 
            get 
            {
                if ( Fake0HopTunnel ) return 100;

                return OutTunnelHops + TunnelMemberHops *
                    ( Config.Pool == TunnelConfig.TunnelPool.Exploratory
                        ? ( MeassuredTunnelBuildTimePerHopSeconds * 2 ) / 3
                        : MeassuredTunnelBuildTimePerHopSeconds );
            }
        }

        public override int LifetimeSeconds 
        { 
            get 
            {
                return TunnelLifetimeSeconds; 
            } 
        }

        public override void Send( TunnelMessage msg )
        {
            throw new NotImplementedException();
        }

        public override void SendRaw( I2NPMessage msg )
        {
            throw new NotImplementedException();
        }

        PeriodicAction FragBufferReport = new PeriodicAction( TickSpan.Seconds( 60 ) );

        public override bool Exectue()
        {
            if ( Terminated || RemoteGateway == null ) return false;

            FragBufferReport.Do( delegate()
            {
                var fbsize = Reassembler.BufferedFragmentCount;
                Logging.Log( $"InboundTunnel {TunnelDebugTrace}: {Destination.Id32Short} Fragment buffer size: {fbsize}" );
                if ( fbsize > 2000 ) throw new Exception( "BufferedFragmentCount > 2000 !" ); // Trying to fill my memory?
            } );

            return HandleReceiveQueue() && HandleSendQueue();
        }

        private bool HandleReceiveQueue()
        {
            II2NPHeader msg = null;
            List<TunnelDataMessage> tdmsgs = null;

            lock ( ReceiveQueue )
            {
                if ( ReceiveQueue.Count == 0 ) return true;

                if ( ReceiveQueue.Any( mq => mq.MessageType == I2NPMessage.MessageTypes.TunnelData ) )
                {
                    var removelist = ReceiveQueue.Where( mq => mq.MessageType == I2NPMessage.MessageTypes.TunnelData );
                    tdmsgs = removelist.Select( mq => (TunnelDataMessage)mq.Message ).ToList();
                    foreach ( var one in removelist.ToArray() ) ReceiveQueue.Remove( one );
                }
                else
                {
                    msg = ReceiveQueue.Last.Value;
                    ReceiveQueue.RemoveLast();
                }
            }

            if ( tdmsgs != null )
            {
                HandleTunnelData( tdmsgs );
                return true;
            }

#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"InboundTunnel {TunnelDebugTrace} HandleReceiveQueue: {msg.MessageType}" );
#endif

            switch ( msg.MessageType )
            {
                case I2NPMessage.MessageTypes.TunnelData:
                    throw new NotImplementedException( "Should not happen " + TunnelDebugTrace );

                case I2NPMessage.MessageTypes.TunnelBuildReply:
                case I2NPMessage.MessageTypes.VariableTunnelBuildReply:
                    ThreadPool.QueueUserWorkItem( cb =>
                    {
                        TunnelProvider.Inst.HandleTunnelBuildReply( (II2NPHeader16)msg );
                    } );
                    return true;

                case I2NPMessage.MessageTypes.DeliveryStatus:
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.LogDebug( $"InboundTunnel {TunnelDebugTrace}: DeliveryStatus: {msg.Message}" );
#endif
                    
                    ThreadPool.QueueUserWorkItem( cb => {
                        lock ( DeliveryStatusReceivedLock )
                        {
                            DeliveryStatusReceived?.Invoke( (DeliveryStatusMessage)msg.Message );
                        }
                    } );
                    break;

                case I2NPMessage.MessageTypes.DatabaseStore:
                    var ds = (DatabaseStoreMessage)msg.Message;
                    ThreadPool.QueueUserWorkItem( cb =>
                    {
                        TunnelProvider.HandleDatabaseStore( ds );
                    } );
                    break;

                case I2NPMessage.MessageTypes.Garlic:
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace}: Garlic: {msg.Message}" );
#endif

                    ThreadPool.QueueUserWorkItem( cb =>
                    {
                        lock ( GarlicMessageReceivedLock )
                        {
                            GarlicMessageReceived?.Invoke( (GarlicMessage)msg.Message );
                        }
                    } );
                    break;

                default:
                    Logging.LogWarning( $"InboundTunnel {TunnelDebugTrace} HandleReceiveQueue: Dropped {msg}" );
                    break;
            }

            return true;
        }

        TunnelDataFragmentReassembly Reassembler = new TunnelDataFragmentReassembly();

        private void HandleTunnelData( List<TunnelDataMessage> msgs )
        {
            DecryptTunnelMessages( msgs );

            var newmsgs = Reassembler.Process( msgs );
            foreach( var one in newmsgs ) 
            {
                if ( one.GetType() == typeof( TunnelMessageLocal ) )
                {
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace} TunnelData distributed Local :\r\n{one.Header}" );
#endif
                    MessageReceived( ( (TunnelMessageLocal)one ).Header );
                }
                else
                if ( one.GetType() == typeof( TunnelMessageRouter ) )
                {
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace} TunnelData distributed Router :\r\n{one.Header}" );
#endif
                    TransportProvider.Send( ( (TunnelMessageRouter)one ).Destination, one.Header.Message );
                }
                else
                if ( one.GetType() == typeof( TunnelMessageTunnel ) )
                {
                    var tone = (TunnelMessageTunnel)one;
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace} TunnelData distributed Tunnel :\r\n{one.Header}" );
#endif
                    var gwmsg = new TunnelGatewayMessage( tone.Header, tone.Tunnel );
                    TransportProvider.Send( tone.Destination, gwmsg );
                }
                else
                {
                    Logging.LogWarning( $"InboundTunnel {TunnelDebugTrace} TunnelData without routing rules:\r\n{one.Header}" );
                }
            }
        }

        private void DecryptTunnelMessages( List<TunnelDataMessage> msgs )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );
            List<TunnelDataMessage> failed = null;

            foreach ( var msg in msgs )
            {
                try
                {
                    for ( int i = TunnelSetup.Hops.Count - 2; i >= 0; --i )
                    {
                        var hop = TunnelSetup.Hops[i];

                        msg.IV.AesEcbDecrypt( hop.IVKey.Key.ToByteArray() );
                        cipher.Decrypt( hop.LayerKey.Key, msg.IV, msg.EncryptedWindow );
                        msg.IV.AesEcbDecrypt( hop.IVKey.Key.ToByteArray() );
                    }

                    // The 0 should be visible now
                    msg.UpdateFirstDeliveryInstructionPosition();
                }
                catch ( Exception ex )
                {
                    Logging.Log( "DecryptTunnelMessages", ex );

                    // Be resiliant to faulty data. Just drop it.
                    if ( failed == null ) failed = new List<TunnelDataMessage>();
                    failed.Add( msg );
                }
            }

            if ( failed != null )
            {
                foreach ( var one in failed ) while ( msgs.Remove( one ) );
            }
        }

        private bool HandleSendQueue()
        {
            lock ( SendQueue )
            {
                if ( SendQueue.Count == 0 ) return true;
            }
            return true;
        }

        public I2NPMessage CreateBuildRequest()
        {
            //TunnelSetup.Hops.Insert( 0, new HopInfo( RouterContext.Inst.MyRouterIdentity ) );

            var vtb = VariableTunnelBuildMessage.BuildInboundTunnel( TunnelSetup );

            //Logging.Log( vtb.ToString() );

            return vtb;
        }

        public override string ToString()
        {
            return $"{base.ToString()} {Destination.Id32Short}";
        }
    }
}
