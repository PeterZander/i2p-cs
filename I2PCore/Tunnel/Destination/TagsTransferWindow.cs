using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;

namespace I2PCore.Tunnel
{
    public delegate void OutboundTunnelSelector( I2PLeaseSet ls, II2NPHeader16 header, GarlicCreationInfo info );

    internal class TagsTransferWindow
    {
        internal static readonly TickSpan GarlicResendTimeLimit = TickSpan.Minutes( 2 );

        internal TickSpan GarlicTimeBetweenResends = TickSpan.Seconds( 15 );

        TickCounter Created = TickCounter.Now;

        internal event Action<GarlicCreationInfo> MessageAckReceived;

        DestinationSession Session;

        TickCounter WaitingForEGAck = null;
        GarlicCreationInfo LatestEGMessage;

        // Tracking id, not message id
        TimeWindowDictionary<uint, GarlicCreationInfo> Window;

        // Message id index
        TimeWindowDictionary<uint, GarlicCreationInfo> OutstandingMessageIds;

        OutboundTunnelSelector TunnelSelector;

        PeriodicAction Resend;

        internal TagsTransferWindow( DestinationSession session, OutboundTunnelSelector tunnelsel )
        {
            Window = new TimeWindowDictionary<uint, GarlicCreationInfo>( GarlicResendTimeLimit );
            OutstandingMessageIds = new TimeWindowDictionary<uint, GarlicCreationInfo>( GarlicTimeBetweenResends * 10 );

            Resend = new PeriodicAction( GarlicTimeBetweenResends / 4 );

            Session = session;
            TunnelSelector = tunnelsel;

            TunnelProvider.DeliveryStatusReceived += new Action<DeliveryStatusMessage>( TunnelProvider_DeliveryStatusReceived );
            InboundTunnel.DeliveryStatusReceived += new Action<DeliveryStatusMessage>( InboundTunnel_DeliveryStatusReceived );
        }

        // Returns a tracking id supplied with events
        internal GarlicCreationInfo Send( bool explack, params GarlicCloveDelivery[] cloves )
        {
            GarlicCreationInfo egmsg = null;

            try
            {
                egmsg = Session.Encrypt( explack, I2NPHeader.GenerateMessageId(), cloves );
                var npmsg = new GarlicMessage( egmsg.Garlic ).GetHeader16( I2NPHeader.GenerateMessageId() );

                if ( explack || WaitingForEGAck != null || egmsg.KeyType == GarlicCreationInfo.KeyUsed.ElGamal )
                {
                    if ( egmsg.KeyType == GarlicCreationInfo.KeyUsed.ElGamal )
                    {
                        LatestEGMessage = egmsg;
                        WaitingForEGAck = TickCounter.Now;
                    }

                    egmsg.LastSend = TickCounter.Now;
                    lock ( Window )
                    {
                        Window[egmsg.TrackingId] = egmsg;
                    }
                }

                if ( egmsg.AckMessageId.HasValue ) lock ( OutstandingMessageIds )
                {
                    OutstandingMessageIds[egmsg.AckMessageId.Value] = egmsg;
                }

                DebugUtils.LogDebug( string.Format( "TagsTransferWindow: Send: ({0}) TrackingId: {1}, Ack MessageId: {2}.",
                    egmsg.KeyType, egmsg.TrackingId, egmsg.AckMessageId ) );

                TunnelSelector( Session.LatestRemoteLeaseSet, npmsg, egmsg );
            }
            catch ( Exception ex )
            {
                Session.Reset();
                DebugUtils.Log( "TagsTransferWindow Send", ex );
            }

            return egmsg;
        }

        internal void Run()
        {
            Resend.Do( () =>
            {
                try
                {
                    List<KeyValuePair<II2NPHeader16, GarlicCreationInfo>> tosend = new List<KeyValuePair<II2NPHeader16, GarlicCreationInfo>>();
                    lock ( Window )
                    {
                        var resend = Window.Where( gci => gci.Value.LastSend.DeltaToNow > GarlicTimeBetweenResends );

                        if ( WaitingForEGAck != null && WaitingForEGAck.DeltaToNow > GarlicTimeBetweenResends )
                        {
                            DebugUtils.LogDebug( "TagsTransferWindow: Run: ElGamal message needs to be resent. Starting over." );
                            Session.Reset();
                        }

                        foreach ( var one in resend.ToArray() )
                        {
                            var egmsg = Session.Encrypt( true, one.Value.TrackingId, one.Value.Cloves );
                            var npmsg = new GarlicMessage( egmsg.Garlic ).GetHeader16( I2NPHeader.GenerateMessageId() );
                            one.Value.LastSend.SetNow();
                            tosend.Add( new KeyValuePair<II2NPHeader16, GarlicCreationInfo>( npmsg, egmsg ) );

                            if ( WaitingForEGAck == null && !one.Value.AckMessageId.HasValue )
                            {
                                // Non explicit ack cloves that should not be retransmitted any more.
                                Window.Remove( one.Key );
                            }
                        }
                    }

                    foreach ( var pair in tosend )
                    {
                        DebugUtils.LogDebug( string.Format( "TagsTransferWindow: Resend: ({0}) TrackingId: {1}, Ack MessageId: {2}.",
                            pair.Value.KeyType, pair.Value.TrackingId, pair.Value.AckMessageId ) );

                        if ( pair.Value.AckMessageId.HasValue ) lock ( OutstandingMessageIds )
                        {
                            OutstandingMessageIds[pair.Value.AckMessageId.Value] = pair.Value;
                        }

                        TunnelSelector( Session.LatestRemoteLeaseSet, pair.Key, pair.Value );
                    }
                }
                catch ( Exception ex )
                {
                    Session.Reset();
                    DebugUtils.Log( "TagsTransferWindow Run", ex );
                }
            } );
        }

        void TunnelProvider_DeliveryStatusReceived( DeliveryStatusMessage dsmsg )
        {
            DeliveryStatusReceived( dsmsg );
        }

        void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage dsmsg )
        {
            DeliveryStatusReceived( dsmsg );
        }

        void DeliveryStatusReceived( DeliveryStatusMessage dsmsg )
        {
            GarlicCreationInfo info;

            lock ( OutstandingMessageIds )
            {
                info = OutstandingMessageIds[dsmsg.MessageId];
                if ( info == null )
                {
                    /*
                    DebugUtils.LogDebug( string.Format( "TagsTransferWindow: DeliveryStatusReceived: Non matching status message MsgId: {0}.",
                        dsmsg.MessageId ) );
                     */
                    return;
                }
                OutstandingMessageIds.Remove( dsmsg.MessageId );
            }

            lock ( Window )
            {
                Window.Remove( info.TrackingId );
            }

            DebugUtils.LogDebug( string.Format( "TagsTransferWindow: DeliveryStatusReceived: Received ACK MsgId: {0} TrackingId: {1}. {2}",
                dsmsg.MessageId, info.TrackingId, info.Created.DeltaToNow ) );

            if ( WaitingForEGAck != null && info.KeyType == GarlicCreationInfo.KeyUsed.ElGamal && dsmsg.MessageId == LatestEGMessage.AckMessageId )
            {
                WaitingForEGAck = null;

                // Aes messages sent after this can be decrypted, hopefully.
                // Send them one more time.
                /*
                lock ( OutstandingMessageIds )
                {
                    var remove = OutstandingMessageIds.Where( gci => gci.Value.KeyType == GarlicCreationInfo.KeyUsed.Aes &&
                        !gci.Value.AckMessageId.HasValue && gci.Value.EGAckMessageId == dsmsg.MessageId );
                    foreach ( var one in remove.ToArray() )
                    {
                        OutstandingMessageIds.Remove( one.Key );
                    }
                }

                lock ( Window )
                {
                    var remove = Window.Where( gci => gci.Value.KeyType == GarlicCreationInfo.KeyUsed.Aes &&
                        !gci.Value.AckMessageId.HasValue && gci.Value.EGAckMessageId == dsmsg.MessageId );
                    foreach ( var one in remove.ToArray() )
                    {
                        Window.Remove( one.Key );
                    }
                }
                 */
            }

            MessageAckEvent( info );
        }

        private void MessageAckEvent( GarlicCreationInfo info )
        {
            if ( MessageAckReceived != null )
            {
                try
                {
                    MessageAckReceived( info );
                }
                catch ( Exception ex )
                {
                    DebugUtils.Log( ex );
                }
            }
        }
    }
}
