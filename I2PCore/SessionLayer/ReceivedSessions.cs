using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Collections.Concurrent;

namespace I2PCore.SessionLayer
{
    public class ReceivedSessions
    {
        readonly I2PPrivateKey PrivateKey;

        TimeWindowDictionary<I2PSessionTag, I2PSessionKey> SessionTags = 
                new TimeWindowDictionary<I2PSessionTag, I2PSessionKey>( TickSpan.Minutes( 15 ) );

        protected CbcBlockCipher Cipher = new CbcBlockCipher( new AesEngine() );
        readonly object Owner;

        public ReceivedSessions( object owner, I2PPrivateKey key )
        {
            Owner = owner;
            PrivateKey = key;
        }

        public Garlic DecryptMessage( EGGarlic message )
        {
            var egdata = message.EGData;

            var (aesblock,sessionkey) = Garlic.RetrieveAESBlock( 
                    egdata, 
                    PrivateKey, 
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

            if ( sessionkey != null && aesblock.Tags.Count > 0 )
            {
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{Owner} ReceivedSessions: {aesblock.Tags.Count} new tags received." );
#endif
                foreach ( var onetag in aesblock.Tags )
                {
                    SessionTags[new I2PSessionTag( new BufRef( onetag ) )] = 
                        sessionkey;
                }
            }

            return new Garlic( (BufRefLen)aesblock.Payload );
        }
    }
}
