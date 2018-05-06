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
    public class EndpointTunnel: Tunnel
    {
        I2PIdentHash NextHop;
        public override I2PIdentHash Destination { get { return NextHop; } }

        internal I2PTunnelId ResponseTunnelId;
        internal uint ResponseMessageId;

        BufLen IVKey;
        BufLen LayerKey;

        internal BandwidthLimiter Limiter;

        public EndpointTunnel( BuildRequestRecord brrec )
            : base( null )
        {
            Config = new TunnelConfig(
                TunnelConfig.TunnelDirection.Inbound,
                TunnelConfig.TunnelPool.External,
                null );

            Limiter = new BandwidthLimiter( Bandwidth.SendBandwidth, TunnelSettings.EndpointTunnelBitrateLimit );

            ReceiveTunnelId = new I2PTunnelId( brrec.ReceiveTunnel );
            ResponseTunnelId = new I2PTunnelId( brrec.NextTunnel );
            ResponseMessageId = brrec.SendMessageId;

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

#if LOG_ALL_TUNNEL_TRANSFER
        ItemFilterWindow<HashedItemGroup> FilterMessageTypes = new ItemFilterWindow<HashedItemGroup>( TickSpan.Seconds( 30 ), 2 );
#endif

        PeriodicAction FragBufferReport = new PeriodicAction( TickSpan.Seconds( 60 ) );

        public override bool Exectue()
        {
            FragBufferReport.Do( delegate()
            {
                var fbsize = Reassembler.BufferedFragmentCount;
                DebugUtils.Log( "EndpointTunnel: " + Destination.Id32Short + " Fragment buffer size: " + fbsize.ToString() );
                if ( fbsize > 2000 ) throw new Exception( "BufferedFragmentCount > 2000 !" ); // Trying to fill my memory?
            } );

            return HandleReceiveQueue();
        }

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

                ReceiveQueue.Clear(); // Just drop the non-TunnelData
            }

            if ( tdmsgs != null )
            {
                return HandleTunnelData( tdmsgs );
            }

            return true;
        }

        TunnelDataFragmentReassembly Reassembler = new TunnelDataFragmentReassembly();

        private bool HandleTunnelData( IEnumerable<TunnelDataMessage> msgs )
        {
            EncryptTunnelMessages( msgs );

            var newmsgs = Reassembler.Process( msgs );
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
                    if ( one.GetType() == typeof( TunnelMessageLocal ) )
                    {
                        MessageReceived( ( (TunnelMessageLocal)one ).Header );
                        DebugUtils.Log( "EndpointTunnel " + Destination.Id32Short + " TunnelData destination Local. Dropped.\r\n" + one.Header.ToString() );
                    }
                    else if ( one.GetType() == typeof( TunnelMessageRouter ) )
                    {
#if LOG_ALL_TUNNEL_TRANSFER
                        if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x2317 ) ) )
                        {
                            DebugUtils.LogDebug( "EndpointTunnel " + Destination.Id32Short + " TunnelData Router :\r\n" + one.Header.MessageType.ToString() );
                        }
#endif
                        var msg = one.Header.Message;
                        Bandwidth.DataSent( msg.Payload.Length );
                        TransportProvider.Send( ( (TunnelMessageRouter)one ).Destination, msg );
                    }
                    else if ( one.GetType() == typeof( TunnelMessageTunnel ) )
                    {
                        var tone = (TunnelMessageTunnel)one;
#if LOG_ALL_TUNNEL_TRANSFER
                        if ( FilterMessageTypes.Update( new HashedItemGroup( Destination, 0x6375 ) ) )
                        {
                            DebugUtils.LogDebug( "EndpointTunnel " + Destination.Id32Short + " TunnelData Tunnel :\r\n" + one.Header.MessageType.ToString() );
                        }
#endif
                        var gwmsg = new TunnelGatewayMessage( tone.Header, tone.Tunnel );

                        Bandwidth.DataSent( gwmsg.Payload.Length );
                        TransportProvider.Send( tone.Destination, gwmsg );
                    }
                    else
                    {
                        DebugUtils.LogDebug( "EndpointTunnel " + Destination.Id32Short + " TunnelData of unexpected type: " + one.Header.ToString() );
                    }
                }
                catch ( Exception ex )
                {
                    DebugUtils.Log( "EndpointTunnel", ex );
                    throw; // Kill tunnel is strange things happen
                }
            }

#if LOG_ALL_TUNNEL_TRANSFER
            if ( dropped > 0 )
            {
                DebugUtils.LogDebug( () => string.Format( "{0} bandwidth limit. {1} dropped messages. {2}", this, dropped, Bandwidth ) );
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
