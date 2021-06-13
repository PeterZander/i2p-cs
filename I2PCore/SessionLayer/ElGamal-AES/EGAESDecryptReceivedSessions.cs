using System.Linq;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Collections.Generic;

namespace I2PCore.SessionLayer
{
    /// <summary>
    /// Decrypts currently received EG/AES Sessions with tags.
    /// </summary>
    public class EGAESDecryptReceivedSessions
    {
        public const int TagLimit = 100;

        public List<I2PPrivateKey> PrivateKeys { get; set; }

        TimeWindowDictionary<I2PSessionTag, I2PSessionKey> SessionTags = 
                new TimeWindowDictionary<I2PSessionTag, I2PSessionKey>( EGAESSessionKeyOrigin.SentTagLifetime );

        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );
        readonly object Owner;

        public EGAESDecryptReceivedSessions( object owner )
        {
            Owner = owner;
        }

        public Garlic DecryptMessage( GarlicMessage message )
        {
            var (aesblock,sessionkey) = Garlic.RetrieveAESBlock(
                    message, 
                    PrivateKeys.First( pk => pk.Certificate.PublicKeyType == I2PKeyType.KeyTypes.ElGamal2048 ), 
                    ( stag ) =>
                    {
                        return SessionTags.TryRemove( stag, out var sessionkeyfound ) ? sessionkeyfound : null;
                    } );

            if ( aesblock is null )
            {
                Logging.LogDebug( $"{Owner} ReceivedSessions: Aes block decrypt failed." );
                return null;
            }

#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{Owner} ReceivedSessions: Working Aes block received. {SessionTags.Count()} tags available." );
#endif

            if ( aesblock?.Tags?.Count > 0 )
            {
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{Owner} ReceivedSessions: {aesblock.Tags.Count} new tags received." );
#endif
                var currenttagcount = SessionTags.Count();

                foreach ( var onetag in aesblock.Tags )
                {
                    if ( currenttagcount >= TagLimit ) break;

                    SessionTags[new I2PSessionTag( new BufRef( onetag ) )] = 
                            aesblock?.NewSessionKey is null 
                                ? sessionkey
                                : aesblock?.NewSessionKey;

                    ++currenttagcount;
                }
            }

            return new Garlic( (BufRefLen)aesblock.Payload );
        }

        public DatabaseLookupKeyInfo KeyGenerator( I2PIdentHash ffrouterid )
        {
            var newtag = new I2PSessionTag();
            var newkey = new I2PSessionKey();
            SessionTags[newtag] = newkey;

            return new DatabaseLookupKeyInfo
            {
                EncryptionFlag = true,
                ECIESFlag = false,
                ReplyKey = newkey.Key,
                Tags = new BufLen[] { newtag.Value }
            };
        }
    }
}
