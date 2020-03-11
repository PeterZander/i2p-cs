using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using System.Diagnostics;

namespace I2PCore.TunnelLayer.I2NP.Data
{
    public class Garlic : I2PType
    {
        public BufLen Data;

        public List<GarlicClove> Cloves = new List<GarlicClove>();

        public Garlic( BufRefLen reader )
        {
            ParseData( reader );
        }

        public Garlic( params GarlicClove[] cloves )
            : this( DefaultRTT(), cloves )
        {
        }

        public Garlic( I2PDate expiration, params GarlicClove[] cloves )
            : this( expiration, cloves.AsEnumerable() )
        {
        }

        public Garlic( IEnumerable<GarlicClove> cloves )
            : this( DefaultRTT(), cloves )
        {
        }

        private static I2PDate DefaultRTT()
        {
            return new I2PDate( DateTime.UtcNow.AddSeconds( 15 ) );
        }

        public Garlic( I2PDate expiration, IEnumerable<GarlicClove> cloves )
        {
            BufRefStream buf = new BufRefStream();
            buf.Write( (byte)cloves.Count() );
            foreach ( var clove in cloves ) clove.Write( buf );

            // Certificate
            buf.Write( new byte[] { 0, 0, 0 } );

            buf.Write( (BufRefLen)BufUtils.Flip32BL( BufUtils.RandomUint() ) );
            expiration.Write( buf );

            Data = new BufLen( buf.ToArray() );
            ParseData( new BufRefLen( Data ) );
        }

        void ParseData( BufRefLen reader )
        {
            var start = new BufLen( reader );

            var cloves = reader.Read8();
            for ( int i = 0; i < cloves; ++i )
            {
                Cloves.Add( new GarlicClove( reader ) );
            }
            reader.Seek( 3 + 4 + 8 ); // Garlic: Cert, MessageId, Expiration

            Data = new BufLen( start, 0, reader - start );
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }

        public override string ToString()
        {
            return $"Garlic: {Cloves?.Count} cloves. {string.Join( ", ", Cloves )}";
        }

        public static EGGarlic EGEncryptGarlic(
                Garlic msg,
                I2PPublicKey pubkey,
                I2PSessionKey sessionkey,
                List<I2PSessionTag> newtags )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            var payload = msg.ToByteArray();
            var dest = new BufLen( new byte[61000] );
            var writer = new BufRefLen( dest, 4 ); // Reserve 4 bytes for GarlicMessageLength

            // ElGamal block
            var egbuf = new BufLen( writer, 0, 222 );
            var sessionkeybuf = new BufLen( egbuf, 0, 32 );
            var preivbuf = new BufLen( egbuf, 32, 32 );
            var egpadding = new BufLen( egbuf, 64 );

            sessionkeybuf.Poke( sessionkey.Key, 0 );
            preivbuf.Randomize();
            egpadding.Randomize();

            var preiv = preivbuf.Clone();

            var eg = new ElGamalCrypto( pubkey );
            eg.Encrypt( writer, egbuf, true );

            // AES block
            var aesstart = new BufLen( writer );
            var aesblock = new GarlicAESBlock( writer, newtags, null, new BufRefLen( payload ) );

            var pivh = I2PHashSHA256.GetHash( preiv );

            cipher.Init( true, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
            cipher.ProcessBytes( aesblock.DataBuf );

            var length = writer - dest;
            dest.PokeFlip32( (uint)( length - 4 ), 0 );

            return new EGGarlic( new BufRefLen( dest, 0, length ) );
        }

        public static (GarlicAESBlock,I2PSessionKey) EGDecryptGarlic( 
                    BufLen egdata, 
                    I2PPrivateKey privkey )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            var egbuf = new BufLen( egdata, 0, 514 );
            var egheader = ElGamalCrypto.Decrypt( egbuf, privkey, true );

            var sessionkey = new I2PSessionKey( new BufLen( egheader, 0, 32 ) );
            var preiv = new BufLen( egheader, 32, 32 );
            var egpadding = new BufLen( egheader, 64 );
            var aesbuf = new BufLen( egdata, 514 );

            var pivh = I2PHashSHA256.GetHash( preiv );

            cipher.Init( false, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
            cipher.ProcessBytes( aesbuf );

            GarlicAESBlock aesblock =
                    new GarlicAESBlock( new BufRefLen( aesbuf ) );

            if ( !aesblock.VerifyPayloadHash() )
            {
                throw new ChecksumFailureException( "AES block hash check failed!" );
            }

            return (aesblock,sessionkey);
        }


        public static EGGarlic AESEncryptGarlic(
                Garlic msg,
                I2PSessionKey sessionkey,
                I2PSessionTag tag,
                List<I2PSessionTag> newtags )
        {
            var cipher = new CbcBlockCipher( new AesEngine() );

            var payload = msg.ToByteArray();
            var dest = new BufLen( new byte[61000] );
            var writer = new BufRefLen( dest, 4 ); // Reserve 4 bytes for GarlicMessageLength

            // Tag as header
            writer.Write( tag.Value );

            // AES block
            var aesstart = new BufLen( writer );
            var aesblock = new GarlicAESBlock( writer, newtags, null, new BufRefLen( payload ) );

            var pivh = I2PHashSHA256.GetHash( tag.Value );

            cipher.Init( true, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
            cipher.ProcessBytes( aesblock.DataBuf );

            var length = writer - dest;
            dest.PokeFlip32( (uint)( length - 4 ), 0 );

            return new EGGarlic( new BufRefLen( dest, 0, length ) );
        }

        public static (GarlicAESBlock,I2PSessionKey) RetrieveAESBlock(
                BufLen egdata,
                I2PPrivateKey privatekey,
                Func<I2PSessionTag,I2PSessionKey> findsessionkey )
        {
            GarlicAESBlock result;

            var cipher = new CbcBlockCipher( new AesEngine() );

            var tag = new I2PSessionTag( new BufRefLen( egdata, 0, 32 ) );
            var sessionkey = findsessionkey?.Invoke( tag );
#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"Garlic: Session key found {sessionkey}" );
#endif
            if ( sessionkey != null )
            {
                var aesbuf = new BufLen( egdata, 32 );
                var pivh = I2PHashSHA256.GetHash( tag.Value );

                cipher.Init( false, sessionkey.Key.ToParametersWithIV( new BufLen( pivh, 0, 16 ) ) );
                cipher.ProcessBytes( aesbuf );

                try
                {
                    result = new GarlicAESBlock( new BufRefLen( aesbuf ) );

                    if ( !result.VerifyPayloadHash() )
                    {
                        Logging.LogDebug( "Garlic: DecryptMessage: AES block SHA256 check failed." );
                        return (null,null);
                    }

                    return (result,sessionkey);
                }
                catch ( ArgumentException ex )
                {
                    Logging.Log( "Garlic", ex );
                }
                catch ( Exception ex )
                {
                    Logging.Log( "Garlic", ex );
                    return (null,null);
                }
            }

#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( "Garlic: No session key. Using ElGamal to decrypt." );
#endif

            try
            {
                (result,sessionkey) = Garlic.EGDecryptGarlic( egdata, privatekey );
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"Garlic: EG session key {sessionkey}" );
#endif
            }
            catch ( Exception ex )
            {
                Logging.LogDebug( "Garlic: ElGamal DecryptMessage failed" );
                Logging.LogDebugData( $"ReceivedSessions {ex}" );
                return (null,null);
            }

            return (result,sessionkey);
        }
    }
}
