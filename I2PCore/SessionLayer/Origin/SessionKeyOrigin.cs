using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public class SessionKeyOrigin
    {
        public int LowWatermarkForNewTags { get; set; } = 7;
        public int NewTagsWhenGenerating { get; set; } = 15;

        class SessionAndTags
        {
            public uint MessageId;
            public I2PSessionKey SessionKey;
            public readonly TimeWindowDictionary<I2PSessionTag, object> Tags =
                new TimeWindowDictionary<I2PSessionTag, object>( TickSpan.Minutes( 12 ) );
        }

        TimeWindowDictionary<uint,SessionAndTags> NotAckedTags =
            new TimeWindowDictionary<uint, SessionAndTags>( TickSpan.Minutes( 2 ) );

        ConcurrentDictionary<I2PSessionKey, SessionAndTags> AckedTags =
            new ConcurrentDictionary<I2PSessionKey, SessionAndTags>();

        readonly I2PDestination MyDestination;
        readonly I2PDestination RemoteDestination;

        public SessionKeyOrigin( I2PDestination mydest, I2PDestination remotedest )
        {
            MyDestination = mydest;
            RemoteDestination = remotedest;

            InboundTunnel.DeliveryStatusReceived += InboundTunnel_DeliveryStatusReceived;
        }

        public void Terminate()
        {
            InboundTunnel.DeliveryStatusReceived -= InboundTunnel_DeliveryStatusReceived;
        }

        protected void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage msg )
        {
            if ( NotAckedTags.TryRemove( msg.StatusMessageId, out var tags ) )
            {
                Logging.LogDebug( $"{this}: SessionKey {tags.SessionKey} ACKed, {tags.Tags.Count} tags." );
                if ( AckedTags.TryGetValue( tags.SessionKey, out var tagpool ) )
                {
                    foreach ( var tag in tags.Tags )
                    {
                        tagpool.Tags[tag.Key] = 1;
                    }
                }
                else
                { 
                    AckedTags[tags.SessionKey] = tags;
                }
            }
        }

        public bool Send( 
                    OutboundTunnel outtunnel, 
                    I2PLease remotelease, 
                    Func<(I2PIdentHash,I2PTunnelId)> replytunnelsel,
                    params GarlicClove[] cloves )
        {
            if ( remotelease is null )
            {
                Logging.LogDebug( $"{this}: No remote lease available." );
                return false;
            }

            if ( outtunnel is null )
            {
                Logging.LogDebug( $"{this}: No outbound tunnels available." );
                return false;
            }

            EGGarlic egmsg = null;

        again:

            if ( !AckedTags.IsEmpty )
            {
                var key = AckedTags.FirstOrDefault().Key;
                if ( AckedTags.TryGetValue( key, out var session ) )
                {
                    List<I2PSessionTag> newtagslist = null;
                    var newcloves = cloves;

                    var availabletags = AckedTags.Sum( t => t.Value.Tags.Count );
                    if ( availabletags <= LowWatermarkForNewTags )
                    {
#if LOG_ALL_LEASE_MGMT
                        Logging.LogDebug( $"{this}: Tag level low {availabletags}. Sending more." );
#endif
                        var newtags = GenerateNewTags( session.SessionKey );
                        newtagslist = newtags.Tags
                                .Select( t => t.Key )
                                .ToList();

                        var (replygw, replytunnel) = replytunnelsel();

                        var ackstatus = new DeliveryStatusMessage( newtags.MessageId );
                        var ackclove = new GarlicClove(
                                        new GarlicCloveDeliveryTunnel(
                                            ackstatus,
                                            replygw, replytunnel ) );

                        newcloves = new List<GarlicClove>( cloves )
                        {
                            ackclove
                        }.ToArray();

                    }
#if LOG_ALL_LEASE_MGMT
                    Logging.LogDebug( $"{this}: Encrypting with session key {session.SessionKey}" );
#endif
                    var tag = session.Tags.First();
                    session.Tags.TryRemove( tag.Key, out _ );
                    if ( session.Tags.IsEmpty )
                    {
                        AckedTags.TryRemove( key, out _ );
                        goto again;
                    }

                    var garlic = new Garlic( newcloves );
                    egmsg = Garlic.AESEncryptGarlic(
                            garlic,
                            session.SessionKey,
                            tag.Key,
                            newtagslist );
                }
            }

            if ( egmsg == null )
            {
                var newtags = GenerateNewTags( new I2PSessionKey() );
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this}: Encrypting with ElGamal to {RemoteDestination} {newtags.SessionKey} {RemoteDestination.PublicKey}, {newtags.MessageId}" );
#endif

                var (replygw, replytunnel) = replytunnelsel();

                var ackstatus = new DeliveryStatusMessage( newtags.MessageId );
                var ackclove = new GarlicClove(
                                new GarlicCloveDeliveryTunnel(
                                    ackstatus,
                                    replygw, replytunnel ) );

                var newcloves = new List<GarlicClove>( cloves )
                {
                    ackclove
                };

                var garlic = new Garlic( newcloves );

                egmsg = Garlic.EGEncryptGarlic(
                        garlic,
                        RemoteDestination.PublicKey,
                        newtags.SessionKey,
                        new List<I2PSessionTag>( newtags.Tags.Select( t => t.Key ) ) );
            }

            outtunnel.Send(
                new TunnelMessageTunnel(
                    new GarlicMessage( egmsg ),
                    remotelease.TunnelGw, remotelease.TunnelId ) );

            return true;
        }

        private SessionAndTags GenerateNewTags( I2PSessionKey sessionkey )
        {
            var result = new List<I2PSessionTag>();

            var sat = new SessionAndTags
            {
                MessageId = I2NPMessage.GenerateMessageId(),
                SessionKey = sessionkey,
            };

            for ( int i = 0; i < NewTagsWhenGenerating; ++i )
            {
                sat.Tags[new I2PSessionTag()] = 1;
            }

            NotAckedTags[sat.MessageId] = sat;

            return sat;
        }

        public override string ToString()
        {
            return $"{GetType().Name} {RemoteDestination}";
        }
    }
}
