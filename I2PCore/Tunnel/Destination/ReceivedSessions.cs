using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel
{
    public class ReceivedSessions
    {
        readonly I2PPrivateKey Key;
        Dictionary<I2PSessionTag, I2PSessionKey> SessionTags = new Dictionary<I2PSessionTag, I2PSessionKey>();
        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );

        public ReceivedSessions( I2PPrivateKey key )
        {
            Key = key;
        }

        public Garlic DecryptMessage( EGGarlic message )
        {
            lock ( SessionTags )
            {
                var old = SessionTags.Where( p => p.Key.Created.DeltaToNow.ToMinutes > ( I2PSessionTag.TagLifetimeMinutes + 2 ) ).ToArray();
                foreach ( var one in old ) SessionTags.Remove( one.Key );
            }

            var egdata = message.EGData;
            var tag = new I2PSessionTag( new BufRefLen( egdata, 0, 32 ) );

            I2PSessionKey sessionkey;
            bool found;

            lock ( SessionTags )
            {
                found = SessionTags.TryGetValue( tag, out sessionkey );
            }

            BufLen aesbuf;

            if ( found )
            {
                aesbuf = new BufLen( egdata, 32 );

                lock ( SessionTags )
                {
                    SessionTags.Remove( tag );
                }

#if LOG_ALL_TUNNEL_TRANSFER
                DebugUtils.LogDebug( "ReceivedSessions: Working tag found for EGarlic." );
#endif

                var pivh = I2PHashSHA256.GetHash( tag.Value );

                Cipher.Init( false, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
                Cipher.ProcessBytes( aesbuf );
            }
            else
            {
                BufLen egheader;
                try
                {
                    var egbuf = new BufLen( egdata, 0, 514 );
                    egheader = ElGamalCrypto.Decrypt( egbuf, Key, true );
                }
                catch ( Exception ex )
                {
                    DebugUtils.Log( "ReceivedSessions", ex );
                    return null;
                }

#if LOG_ALL_TUNNEL_TRANSFER
                DebugUtils.LogDebug( "ReceivedSessions: Using ElGamal to decrypt." );
#endif

                sessionkey = new I2PSessionKey( new BufLen( egheader, 0, 32 ) );
                var preiv = new BufLen( egheader, 32, 32 );
                var egpadding = new BufLen( egheader, 64 );
                aesbuf = new BufLen( egdata, 514 );

                var pivh = I2PHashSHA256.GetHash( preiv );

                Cipher.Init( false, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
                Cipher.ProcessBytes( aesbuf );
            }

            GarlicAESBlock aesblock;

            try
            {
                aesblock = new GarlicAESBlock( new BufRefLen( aesbuf ) );
            }
            catch ( Exception ex )
            {
                DebugUtils.Log( "ReceivedSessions", ex );
                return null;
            }

            if ( !aesblock.VerifyPayloadHash() )
            {
                DebugUtils.LogDebug( "ReceivedSessions: DecryptMessage: AES block SHA256 check failed." );
                return null;
            }

#if LOG_ALL_TUNNEL_TRANSFER
            DebugUtils.LogDebug( "ReceivedSessions: Working Aes block received. " + SessionTags.Count.ToString() + " tags available." );
#endif

            if ( aesblock.Tags.Count > 0 )
            {
#if LOG_ALL_TUNNEL_TRANSFER
                DebugUtils.LogDebug( "ReceivedSessions: " + aesblock.Tags.Count.ToString() + " new tags received." );
#endif
                lock ( SessionTags )
                {
                    foreach ( var onetag in aesblock.Tags.ToArray() ) SessionTags[new I2PSessionTag( new BufRef( onetag ) )] = sessionkey;
                }
            }

            return new Garlic( (BufRefLen)aesblock.Payload );
        }
    }
}
