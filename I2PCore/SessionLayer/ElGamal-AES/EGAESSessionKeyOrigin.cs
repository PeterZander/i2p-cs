using System;
using System.Linq;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Collections.Generic;

namespace I2PCore.SessionLayer
{
    /// <summary>
    /// Source of new EG/AES sessions / tag generation
    /// </summary>
    public class EGAESSessionKeyOrigin
    {
        public static TickSpan SentTagLifetime = TickSpan.Minutes( 30 );
        public static TickSpan ACKedTagLifetime = SentTagLifetime - TickSpan.Minutes( 3 );
        public static TickSpan UnACKedTagLifetime = TickSpan.Minutes( 3 );

        public virtual int LowWatermarkForNewTags
        {
            get => Context.LowWatermarkForNewTags;
            set => Context.LowWatermarkForNewTags = value;
        }

        public virtual int NewTagsWhenGenerating
        {
            get => Context.NewTagsWhenGenerating;
            set => Context.NewTagsWhenGenerating = value;
        }

        class SessionAndTags
        {
            /// <summary>MessageId of the DeliveryStatusMessage of the ACK.</summary>
            public uint MessageId;
            public I2PSessionKey SessionKey;
            public readonly TimeWindowDictionary<I2PSessionTag, object> Tags =
                new TimeWindowDictionary<I2PSessionTag, object>( SentTagLifetime );
        }

        TimeWindowDictionary<uint,SessionAndTags> NotAckedTags =
            new TimeWindowDictionary<uint, SessionAndTags>( UnACKedTagLifetime );

        TimeWindowDictionary<I2PSessionKey, SessionAndTags> AckedTags =
            new TimeWindowDictionary<I2PSessionKey, SessionAndTags>( ACKedTagLifetime );

        readonly ClientDestination Context;
        readonly I2PDestination MyDestination;
        readonly I2PIdentHash RemoteDestination;

        internal EGAESSessionKeyOrigin( ClientDestination context, I2PDestination mydest, I2PIdentHash remotedest )
        {
            Context = context;
            MyDestination = mydest;
            RemoteDestination = remotedest;
        }

        internal void DeliveryStatusReceived( DeliveryStatusMessage msg, InboundTunnel from )
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

        public GarlicMessage Encrypt(
                    IEnumerable<I2PPublicKey> remotepublickeys,
                    InboundTunnel replytunnel,
                    IList<GarlicClove> cloves )
        {
            var ( sessiontag, sessionkey ) = PopAckedTag();
            if ( sessionkey != null )
                return EncryptAES(
                    sessiontag,
                    sessionkey,
                    replytunnel,
                    cloves );

            return EncryptEG( remotepublickeys, replytunnel, cloves );
        }

        protected GarlicMessage EncryptEG(
                    IEnumerable<I2PPublicKey> remotepublickeys,
                    InboundTunnel replytunnel,
                    IList<GarlicClove> cloves )
        {
            var newtags = GenerateNewTags();
#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this}: Encrypting with ElGamal to {RemoteDestination.Id32Short} {newtags.SessionKey}, {newtags.MessageId}" );
#endif

            var garlic = new Garlic( cloves );

            // Use enum value as priority
            var pkey = remotepublickeys
                        .Where( pk => pk.Certificate.PublicKeyType == I2PKeyType.KeyTypes.ElGamal2048 ) // TODO: Currently supported
                        .OrderByDescending( pk => (ushort)pk.Certificate.PublicKeyType )
                        .FirstOrDefault();

            return Garlic.EGEncryptGarlic(
                    garlic,
                    pkey,
                    newtags.SessionKey,
                    newtags.Tags.Select( t => t.Key ).ToList() );
        }

        protected GarlicMessage EncryptAES(
                I2PSessionTag sessiontag,
                I2PSessionKey sessionkey,
                InboundTunnel replytunnel,
                IList<GarlicClove> cloves )
        {
            SessionAndTags newsessionandtags = null;

            var availabletags = AckedTags.Sum( t => t.Value.Tags.Count );
            if ( availabletags <= LowWatermarkForNewTags )
            {
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this}: Tag level low {availabletags}. Sending more." );
#endif
                newsessionandtags = GenerateNewTags();

                var ackstatus = new DeliveryStatusMessage( newsessionandtags.MessageId );
                var ackclove = new GarlicClove(
                                new GarlicCloveDeliveryTunnel(
                                    ackstatus,
                                    replytunnel.Destination, replytunnel.GatewayTunnelId ) );
                cloves.Add( ackclove );
            }

#if LOG_ALL_LEASE_MGMT
            Logging.LogInformation( $"{this}: Encrypting with session key {sessionkey}" );
#endif

            var garlic = new Garlic( cloves );
            return Garlic.AESEncryptGarlic(
                    garlic,
                    sessionkey,
                    sessiontag,
                    newsessionandtags?.SessionKey,
                    newsessionandtags?.Tags.Select( t => t.Key ).ToList() );
        }

        ( I2PSessionTag, I2PSessionKey ) PopAckedTag()
        {
        again:
            var onekey = AckedTags.Random();
            if ( BufUtils.EqualsDefaultValue( onekey ) )
                    return ( null, null );

            var session = onekey.Value;

            var tag = session.Tags.FirstOrDefault();
            if ( BufUtils.EqualsDefaultValue( tag ) )
            {
                AckedTags.Remove( onekey.Key );
                goto again;
            }

            session.Tags.Remove( tag.Key );
            if ( session.Tags.IsEmpty )
            {
                AckedTags.Remove( onekey.Key );
            }
            else
            {
                AckedTags.Touch( onekey.Key );
            }

            return ( tag.Key, session.SessionKey );
        }

        private SessionAndTags GenerateNewTags()
        {
            var result = new List<I2PSessionTag>();
            var sessionkey = new I2PSessionKey();

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
            return $"{Context} {GetType().Name} {RemoteDestination.Id32Short}";
        }
    }
}
