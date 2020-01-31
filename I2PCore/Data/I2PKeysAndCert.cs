using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Security;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PKeysAndCert : I2PType
    {
        public BufLen PublicKeyBuf { get { return new BufLen( Data, 0, Certificate.PublicKeyLength ); } }
        public I2PPublicKey PublicKey { 
            get 
            { 
                return new I2PPublicKey( (BufRefLen)PublicKeyBuf, Certificate ); 
            } 
            
            set 
            { 
                ( (BufRefLen)PublicKeyBuf ).Write( value.ToByteArray() ); 
            } 
        }

        public BufLen Padding
        {
            get
            {
                return new BufLen( Data, 256 + 128 - Math.Min( 128, Certificate.SigningPublicKeyLength ),
                    Certificate.SigningPublicKeyLength );
            }
        }

        public BufLen SigningPublicKeyBuf
        {
            get
            {
                return new BufLen(
                    Data,
                    256 + 128 - Math.Min( 128, Certificate.SigningPublicKeyLength ), Certificate.SigningPublicKeyLength );
            }
        }

        public BufLen SigningPublicKeyPadding
        {
            get
            {
                return new BufLen(
                    Data, 256, 128 - Math.Min( 128, Certificate.SigningPublicKeyLength ) );
            }
        }

        public I2PSigningPublicKey SigningPublicKey
        {
            get
            {
                return new I2PSigningPublicKey( (BufRefLen)SigningPublicKeyBuf, Certificate );
            }

            set
            {
                SigningPublicKeyPadding.Randomize();
                var writer = (BufRefLen)SigningPublicKeyBuf;
                writer.Write( value.ToByteArray() );
            }
        }

        public BufLen CertificateBuf { get { return new BufLen( Data, 256 + 128, new I2PCertificate( new BufRef( Data, 256 + 128 ) ).CertLength ); } }
        public I2PCertificate Certificate
        {
            get
            {
                return new I2PCertificate( new BufRef( Data, 256 + 128 ) );
            }

            protected set
            {
                var writer = new BufRef( CertificateBuf ); // We know the length
                var ar = value.ToByteArray();
                writer.Write( ar );
            }
        }

        BufLen Data;

        public I2PKeysAndCert( I2PPublicKey pubkey, I2PSigningPublicKey signkey )
        {
            Data = new BufLen( new byte[RecordSize( signkey.Certificate )] );

            Data.Randomize();
            Certificate = signkey.Certificate;
            PublicKey = pubkey;
            SigningPublicKey = signkey;
        }

        private int RecordSize( I2PCertificate cert )
        {
            return 256 + 128 + cert.CertLength;
        }

        public I2PKeysAndCert( BufRef reader )
        {
            var cert = new I2PCertificate( new BufRef( reader, 256 + 128 ) );
            Data = reader.ReadBufLen( RecordSize( cert ) );
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }

        I2PIdentHash CachedIdentHash;

        public I2PIdentHash IdentHash
        {
            get
            {
                if ( CachedIdentHash != null ) return CachedIdentHash;
                CachedIdentHash = new I2PIdentHash( this );
                return CachedIdentHash;
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PKeysAndCert" );
            result.AppendLine( "PublicKey - " + PublicKey.ToString() );
            result.AppendLine( "SigningPublicKey - " + SigningPublicKey.ToString() );
            result.AppendLine( "Certificate      : " + Certificate.ToString() );

            return result.ToString();
        }
    }
}
