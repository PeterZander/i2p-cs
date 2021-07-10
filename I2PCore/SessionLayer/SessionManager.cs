using System;
using I2PCore.Data;
using System.Collections.Generic;
using System.Collections.Concurrent;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{

    /// <summary>
    /// Manages temporary crypto keys and sessions with remote destinations
    /// and encrypts and decrypts communication.
    /// </summary>
    public class SessionManager
    {
        /// <summary>
        /// Used to decrypt ElGamal blocks.
        /// </summary>
        public List<I2PPrivateKey> PrivateKeys 
        { 
            get => PrivateKeysField; 
            set
            {
                PrivateKeysField = value;
                IncommingSessions.PrivateKeys = value;
            }
        }
        private List<I2PPrivateKey> PrivateKeysField;

        /// <summary>
        /// Used when constructing LeaseSets for this Destination.
        /// </summary>
        public List<I2PPublicKey> PublicKeys { get; set; }

        internal readonly ConcurrentDictionary<I2PIdentHash,Session> Sessions =
                new ConcurrentDictionary<I2PIdentHash,Session>();

        EGAESDecryptReceivedSessions IncommingSessions;

        internal readonly ClientDestination Owner;

        public SessionManager( ClientDestination owner )
        {
            Owner = owner;
            IncommingSessions = new EGAESDecryptReceivedSessions( this );
        }

        public void GenerateTemporaryKeys()
        {
            var tmpprivkey = new I2PPrivateKey( new I2PCertificate( I2PKeyType.KeyTypes.ElGamal2048 ) );
            var tmppubkey = new I2PPublicKey( tmpprivkey );

            PrivateKeys = new List<I2PPrivateKey>() { tmpprivkey };
            PublicKeys = new List<I2PPublicKey>() { tmppubkey };
        }

        public Garlic DecryptMessage( GarlicMessage message )
        {
            return IncommingSessions.DecryptMessage( message );
        }

        Session GetSession( I2PIdentHash dest )
        {
            return Sessions.GetOrAdd(
                        dest,
                        ( d ) => new Session(
                                    this,
                                    Owner.Destination,
                                    dest ) );
        }
        public GarlicMessage Encrypt(
            I2PIdentHash dest,
            IEnumerable<I2PPublicKey> remotepublickeys,
            ILeaseSet publishedleases,
            InboundTunnel replytunnel,
            params GarlicClove[] cloves )
        {
            var sess = GetSession( dest );
            return sess.Encrypt( remotepublickeys, publishedleases, replytunnel, cloves );
        }

        public void MySignedLeasesUpdated()
        {
            foreach( var sess in Sessions )
            {
                sess.Value.MySignedLeasesUpdated( sess.Key );
            }
        }

        public void LeaseSetReceived( ILeaseSet ls )
        {
            if ( ls.Destination.IdentHash == Owner.Destination.IdentHash )
            {
                // that is me
                Logging.LogDebug(
                    $"{this}: Sessions: LeaseSetReceived: " +
                    $"discarding my lease set." );
                return;
            }

            if ( ls.Expire < DateTime.UtcNow )
            {
                Logging.LogDebug(
                    $"{this}: Sessions: LeaseSetReceived: " +
                    $"discarding expired lease set. {ls}" );
                return;
            }

            var sess = GetSession( ls.Destination.IdentHash );
            sess.LeaseSetReceived( ls );
        }

        public ILeaseSet GetLeaseSet( I2PIdentHash dest )
        {
            var sess = GetSession( dest );

            if ( sess?.RemoteLeaseSet is null )
            {
                var cachedls = NetDb.Inst.FindLeaseSet( dest );
                if ( cachedls != null )
                {
                    LeaseSetReceived( cachedls );
                    return cachedls;
                }

                return null;
            }

            return sess.RemoteLeaseSet;
        }

        public ILease GetTunnelPair( I2PIdentHash dest, OutboundTunnel outtunnel )
        {
            var sess = GetSession( dest );
            return sess.GetTunnelPair( outtunnel );
        }

        internal void DataSentToRemote( I2PIdentHash dest )
        {
            if ( Sessions.TryGetValue( dest, out var sess ) )
            {
                sess.DataSentToRemote( dest );
            }
        }

        public void RemoteIsActive( I2PIdentHash dest )
        {
            if ( Sessions.TryGetValue( dest, out var sess ) )
            {
                sess.RemoteIsActive( dest );
            }
        }

        public DatabaseLookupKeyInfo KeyGenerator( I2PIdentHash ffrouterid )
        {
            return IncommingSessions.KeyGenerator( ffrouterid );
        }

        public override string ToString()
        {
            return $"{Owner} {GetType().Name}";
        }
    }
}