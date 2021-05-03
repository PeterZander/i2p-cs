using System;
using System.IO;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PCertificate : I2PType
    {
        public enum CertTypes: byte { NULL = 0, HASHCASH = 1, HIDDEN = 2, SIGNED = 3, MULTIPLE = 4, KEY = 5 }

        public CertTypes CType { get { return (CertTypes)Data[0]; } set { Data[0] = (byte)value; } }
        public I2PSigningKey.SigningKeyTypes KEYSignatureType
        {
            get
            {
                if ( Payload.Length < 4 ) return I2PSigningKey.SigningKeyTypes.Invalid;
                return (I2PSigningKey.SigningKeyTypes)Payload.PeekFlip16( 0 );
            }
            protected set
            {
                if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                Payload.PokeFlip16( (ushort)value, 0 );
            }
        }

        public I2PSigningKey.KeyTypes KEYPublicKeyType
        {
            get
            {
                if ( Payload.Length < 4 ) return I2PKeyType.KeyTypes.Invalid;
                return (I2PSigningKey.KeyTypes)Payload.PeekFlip16( 2 );
            }
            protected set
            {
                if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                Payload.PokeFlip16( (ushort)value, 2 );
            }
        }

        int NotImplementedPublicKeyLength;

        public ushort PayloadLength { get { return Data.PeekFlip16( 1 ); } protected set { Data.PokeFlip16( value, 1 ); } }
        public BufLen Payload { get { return new BufLen( Data, 3, PayloadLength ); } }
        public BufLen PayloadExtraKeySpace 
        { 
            get 
            {
                if ( PayloadLength < 4 ) return null;
                return new BufLen( Data, 7, PayloadLength - 4 ); 
            } 
        }

        BufLen Data;

        public I2PCertificate()
        {
            Data = new BufLen( new byte[3] );
            CType = CertTypes.NULL;
        }

        public I2PCertificate( I2PSigningKey.SigningKeyTypes signkeytype )
        {
            ushort pllen = 0;

            switch ( signkeytype )
            {
                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    pllen = 4;
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    pllen = 4 + 4;
                    break;
            }

            switch ( signkeytype )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    Data = new BufLen( new byte[3] );
                    PayloadLength = pllen;
                    CType = CertTypes.NULL;
                    break;

                default:
                    Data = new BufLen( new byte[3 + pllen] );
                    PayloadLength = pllen;
                    CType = CertTypes.KEY;
                    KEYSignatureType = signkeytype;
                    break;
            }
        }

        public I2PCertificate( I2PPublicKey.KeyTypes keytype, int keylen = -1 )
        {
            Data = new BufLen( new byte[7] { (byte)CertTypes.KEY, 2, 0, 0, 0, 0, 0 } );

            switch ( keytype )
            {
                case I2PKeyType.KeyTypes.ElGamal2048:
                case I2PKeyType.KeyTypes.P256:
                case I2PKeyType.KeyTypes.P384:
                case I2PKeyType.KeyTypes.P521:
                case I2PKeyType.KeyTypes.X25519:
                    KEYPublicKeyType = keytype;
                    break;

                default:
                    KEYPublicKeyType = I2PKeyType.KeyTypes.NotImplemented;
                    NotImplementedPublicKeyLength = keylen;
                    Logging.LogWarning( $"I2PCertificate: Public key type {keytype} not implemented" );
                    break;
            }
        }

        public I2PCertificate( BufRef buf )
        {
            Data = new BufLen( buf, 0, 3 ); // Get CertLength
            Data = buf.ReadBufLen( CertLength );
        }

        public I2PSigningKey.SigningKeyTypes SignatureType
        {
            get
            {
                switch ( CType )
                {
                    case CertTypes.NULL:
                        return I2PSigningKey.SigningKeyTypes.DSA_SHA1;

                    case CertTypes.KEY:
                        if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                        return (I2PSigningKey.SigningKeyTypes)Payload.PeekFlip16( 0 );

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public I2PKeyType.KeyTypes PublicKeyType
        {
            get
            {
                switch ( CType )
                {
                    case CertTypes.NULL:
                        return I2PKeyType.KeyTypes.ElGamal2048;

                    case CertTypes.KEY:
                        if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                        return (I2PKeyType.KeyTypes)Payload.PeekFlip16( 2 );

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public int CertLength { get { return 3 + PayloadLength; } }

        public int PublicKeyLength
        {
            get
            {
                var pkt = PublicKeyType;
                if ( pkt == I2PKeyType.KeyTypes.NotImplemented ) return NotImplementedPublicKeyLength;

                return I2PKeyType.PublicKeyLength( pkt );
            }
        }
        public int PrivateKeyLength
        {
            get
            {
                return I2PKeyType.PrivateKeyLength( PublicKeyType );
            }
        }

        public int SigningPublicKeyLength
        {
            get
            {
                switch ( CType )
                {
                    case CertTypes.NULL:
                        return 128;

                    case CertTypes.KEY:
                        return I2PSigningKey.SigningPublicKeyLength( SignatureType );

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public int SigningPrivateKeyLength
        {
            get
            {
                switch ( CType )
                {
                    case CertTypes.NULL:
                        return 20;

                    case CertTypes.KEY:
                        return I2PSigningKey.SigningPrivateKeyLength( SignatureType );

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public int SignatureLength
        {
            get
            {
                switch ( CType )
                {
                    case CertTypes.NULL:
                        return 40;

                    case CertTypes.KEY:
                        return I2PSigningKey.SignatureLength( SignatureType );

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public int RouterIdentitySize
        {
            get
            {
                return PublicKeyLength + 128 + CertLength;
            }
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }

        public override string ToString()
        {
            return $"{CType} {PublicKeyType} {SignatureType}";
        }
    }
}
