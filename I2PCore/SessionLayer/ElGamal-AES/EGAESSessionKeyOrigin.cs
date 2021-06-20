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
        public static readonly TickSpan SentTagLifetime = TickSpan.Minutes( 30 );
        public static readonly TickSpan ACKedTagLifetime = SentTagLifetime - TickSpan.Minutes( 3 );
        public static readonly TickSpan UnACKedTagLifetime = TickSpan.Minutes( 3 );

        public int LowWatermarkForNewTags
        {
            get => Owner?.LowWatermarkForNewTags ?? 7;
            set => Owner.LowWatermarkForNewTags = value;
        }

        public int NewTagsWhenGenerating
        {
            get => Owner?.NewTagsWhenGenerating ?? 15;
            set => Owner.NewTagsWhenGenerating = value;
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

        readonly ClientDestination Owner;
        readonly I2PDestination MyDestination;
        readonly I2PIdentHash RemoteDestination;

        public EGAESSessionKeyOrigin( ClientDestination owner, I2PDestination mydest, I2PIdentHash remotedest )
        {
            Owner = owner;
            MyDestination = mydest;
            RemoteDestination = remotedest;

            Router.DeliveryStatusReceived += Router_DeliveryStatusReceived;
        }

        public void Terminate()
        {
            Router.DeliveryStatusReceived -= Router_DeliveryStatusReceived;
        }

        protected void Router_DeliveryStatusReceived( DeliveryStatusMessage msg, InboundTunnel from )
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
                    ILeaseSet publishedleases,
                    InboundTunnel replytunnel,
                    bool needsleaseupdate,
                    params GarlicClove[] cloves )
        {
            var ( sessiontag, sessionkey ) = PopAckedTag();
            if ( sessionkey != null )
                return EncryptAES(
                    sessiontag,
                    sessionkey,
                    publishedleases,
                    replytunnel,
                    needsleaseupdate,
                    cloves );

            return EncryptEG( remotepublickeys, publishedleases, replytunnel, cloves );
        }

        protected GarlicMessage EncryptEG(
                    IEnumerable<I2PPublicKey> remotepublickeys,
                    ILeaseSet publishedleases,
                    InboundTunnel replytunnel,
                    params GarlicClove[] cloves )
        {
            var newtags = GenerateNewTags();
#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this}: Encrypting with ElGamal to {RemoteDestination} {newtags.SessionKey}, {newtags.MessageId}" );
#endif

            var myleases = new DatabaseStoreMessage( publishedleases );
            var ackstatus = new DeliveryStatusMessage( newtags.MessageId );

            var newcloves = new List<GarlicClove>
            {
                new GarlicClove(
                            new GarlicCloveDeliveryDestination(
                                myleases,
                                RemoteDestination ) ),
                new GarlicClove(
                            new GarlicCloveDeliveryTunnel(
                                ackstatus,
                                replytunnel.Destination, replytunnel.GatewayTunnelId ) ),
            };

            newcloves.AddRange( cloves );

            var garlic = new Garlic( newcloves );

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
                ILeaseSet publishedleases,
                InboundTunnel replytunnel,
                bool needsleaseupdate,
                params GarlicClove[] cloves )
        {
            SessionAndTags newsessionandtags = null;
            var newcloves = cloves;

            var availabletags = AckedTags.Sum( t => t.Value.Tags.Count );
            if ( availabletags <= LowWatermarkForNewTags )
            {
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this}: Tag level low {availabletags}. Sending more." );
#endif
                newsessionandtags = GenerateNewTags();

                var newcloveslist = new List<GarlicClove>( cloves );

                var ackstatus = new DeliveryStatusMessage( newsessionandtags.MessageId );
                var ackclove = new GarlicClove(
                                new GarlicCloveDeliveryTunnel(
                                    ackstatus,
                                    replytunnel.Destination, replytunnel.GatewayTunnelId ) );
                newcloveslist.Add( ackclove );

                if ( needsleaseupdate )
                {
                    Logging.LogDebug( $"{this}: Sending my leases to remote {RemoteDestination.Id32Short}." );

                    var myleases = new DatabaseStoreMessage( publishedleases );
                    newcloveslist.Add(
                        new GarlicClove(
                            new GarlicCloveDeliveryDestination(
                                myleases,
                                RemoteDestination ) ) );
                }

                newcloves = newcloveslist.ToArray();
            }
#if LOG_ALL_LEASE_MGMT
            Logging.LogInformation( $"{this}: Encrypting with session key {sessionkey}" );
#endif

            var garlic = new Garlic( newcloves );
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
            return $"{GetType().Name} {RemoteDestination}";
        }
    }
}
