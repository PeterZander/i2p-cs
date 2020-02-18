﻿using System;
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

        public readonly uint TunnelBuildReplyMessageId = BufUtils.RandomUint();
        public readonly int OutTunnelHops;

        static readonly object DeliveryStatusReceivedLock = new object();
        public static event Action<DeliveryStatusMessage> DeliveryStatusReceived;

        readonly object GarlicMessageReceivedLock = new object();
        public event Action<GarlicMessage> GarlicMessageReceived;

        public InboundTunnel( ITunnelOwner owner, TunnelConfig config, int outtunnelhops )
            : base( owner, config )
        {
            OutTunnelHops = outtunnelhops;

            var gw = config.Info.Hops[0];
            RemoteGateway = gw.Peer.IdentHash;
            GatewayTunnelId = gw.TunnelId;

            ReceiveTunnelId = config.Info.Hops.Last().TunnelId;

#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"InboundTunnel: Tunnel {Destination?.Id32Short} created." );
#endif
        }

        // Fake 0-hop
        protected InboundTunnel( ITunnelOwner owner, TunnelConfig config )
            : base( owner, config )
        {
            Established = true;

            ReceiveTunnelId = config.Info.Hops.Last().TunnelId;
            RemoteGateway = RouterContext.Inst.MyRouterIdentity.IdentHash;
            GatewayTunnelId = ReceiveTunnelId;

            Logging.LogDebug( $"InboundTunnel {TunnelDebugTrace}: 0-hop tunnel {Destination?.Id32Short} created." );
        }

        public override IEnumerable<I2PRouterIdentity> TunnelMembers 
        {
            get
            {
                if ( Config?.Info is null ) return null; 
                return Config.Info.Hops.Select( h => (I2PRouterIdentity)h.Peer );
            }
        }

        public override TickSpan TunnelEstablishmentTimeout 
        { 
            get 
            {
                var hops = OutTunnelHops + TunnelMemberHops;
                var timeperhop = Config.Pool == TunnelConfig.TunnelPool.Exploratory
                        ? ( MeassuredTunnelBuildTimePerHop * 2 ) / 3
                        : MeassuredTunnelBuildTimePerHop;

                return timeperhop * hops;
            }
        }

        public override void Send( TunnelMessage msg )
        {
            throw new NotImplementedException();
        }

        PeriodicAction FragBufferReport = new PeriodicAction( TickSpan.Seconds( 60 ) );

        public override bool Exectue()
        {
            if ( Terminated || RemoteGateway == null )
            {
                return false;
            }

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
            List<TunnelDataMessage> tdmsgs = null;

            while ( !ReceiveQueue.IsEmpty )
            {
                if ( !ReceiveQueue.TryDequeue( out var msg ) ) continue;

                if ( msg.MessageType != I2NPMessage.MessageTypes.TunnelData )
                {
                    HandleTunnelMessage( msg );
                }
                else
                {
                    if ( tdmsgs is null ) tdmsgs = new List<TunnelDataMessage>();
                    tdmsgs.Add( (TunnelDataMessage)msg.Message );
                }
            }

            if ( tdmsgs != null )
            {
                HandleTunnelData( tdmsgs );
            }

            return true;
        }

        private bool HandleTunnelMessage( II2NPHeader msg )
        {
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"InboundTunnel {TunnelDebugTrace} HandleReceiveQueue: {msg.MessageType}" );
#endif

            switch ( msg.MessageType )
            {
                case I2NPMessage.MessageTypes.TunnelData:
                    throw new NotImplementedException( $"Should not happen {TunnelDebugTrace}" );

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

                    ThreadPool.QueueUserWorkItem( cb =>
                    {
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
                var onetype = one.GetType();

                if ( onetype == typeof( TunnelMessageLocal ) )
                {
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace} TunnelData distributed Local :\r\n{one.Header}" );
#endif
                    MessageReceived( ( (TunnelMessageLocal)one ).Header );
                }
                else
                if ( onetype == typeof( TunnelMessageRouter ) )
                {
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"InboundTunnel {TunnelDebugTrace} TunnelData distributed Router :\r\n{one.Header}" );
#endif
                    TransportProvider.Send( ( (TunnelMessageRouter)one ).Destination, one.Header.Message );
                }
                else
                if ( onetype == typeof( TunnelMessageTunnel ) )
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
                    for ( int i = Config.Info.Hops.Count - 2; i >= 0; --i )
                    {
                        var hop = Config.Info.Hops[i];

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
            return true;
        }

        public I2NPMessage CreateBuildRequest()
        {
            //TunnelSetup.Hops.Insert( 0, new HopInfo( RouterContext.Inst.MyRouterIdentity ) );

            var vtb = VariableTunnelBuildMessage.BuildInboundTunnel( Config.Info );

            //Logging.Log( vtb.ToString() );

            return vtb;
        }

        public override string ToString()
        {
            return $"{base.ToString()} {Destination.Id32Short}";
        }
    }
}
