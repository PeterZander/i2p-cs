using I2PCore.Data;
using System.Collections.Generic;
using System.Collections.Concurrent;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;

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

        protected readonly ConcurrentDictionary<I2PIdentHash, EGAESSessionKeyOrigin> SessionKeys =
                new ConcurrentDictionary<I2PIdentHash, EGAESSessionKeyOrigin>();

        EGAESDecryptReceivedSessions IncommingSessions;

        readonly ClientDestination Owner;
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

        public GarlicMessage Encrypt(
            I2PIdentHash dest,
            IEnumerable<I2PPublicKey> remotepublickeys,
            ILeaseSet publishedleases,
            InboundTunnel replytunnel,
            bool needsleaseupdate,
            params GarlicClove[] cloves )
        {
            var sk = SessionKeys.GetOrAdd(
                        dest,
                        ( d ) => new EGAESSessionKeyOrigin(
                                    Owner,
                                    Owner.Destination,
                                    dest ) );

            return sk.Encrypt(
                    remotepublickeys,
                    publishedleases,
                    replytunnel,
                    needsleaseupdate,
                    cloves );
        }


        public DatabaseLookupKeyInfo KeyGenerator( I2PIdentHash ffrouterid )
        {
            return IncommingSessions.KeyGenerator( ffrouterid );
        }
    }
}