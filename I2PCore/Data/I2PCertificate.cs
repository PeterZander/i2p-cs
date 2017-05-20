using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using I2PCore.Data;

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
                if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
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
                if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                return (I2PSigningKey.KeyTypes)Payload.PeekFlip16( 2 );
            }
            protected set
            {
                if ( Payload.Length < 4 ) throw new InvalidDataException( "Cert payload not 4 bytes for Key cert!" );
                Payload.PokeFlip16( (ushort)value, 2 );
            }
        }

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
            switch ( signkeytype )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    Data = new BufLen( new byte[3] );
                    CType = CertTypes.NULL;
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    Data = new BufLen( new byte[3 + 4] );
                    CType = CertTypes.KEY;
                    PayloadLength = 4;
                    KEYSignatureType = I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256;
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    Data = new BufLen( new byte[3 + 4] );
                    CType = CertTypes.KEY;
                    PayloadLength = 4;
                    KEYSignatureType = I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384;
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    Data = new BufLen( new byte[3 + 4 + 4] );
                    CType = CertTypes.KEY;
                    PayloadLength = 4 + 4;
                    KEYSignatureType = I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521;
                    break;

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    Data = new BufLen( new byte[3 + 4] );
                    CType = CertTypes.KEY;
                    PayloadLength = 4;
                    KEYSignatureType = I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519;
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public I2PCertificate( BufRef buf )
        {
            Data = new BufLen( buf, 0, 3 );
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
                switch ( PublicKeyType )
                {
                    case I2PKeyType.KeyTypes.ElGamal2048:
                        return 256;

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public int PrivateKeyLength
        {
            get
            {
                switch ( PublicKeyType )
                {
                    case I2PKeyType.KeyTypes.ElGamal2048:
                        return 256;

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
                        switch ( SignatureType )
                        {
                            case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                                return 20;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                                return 32;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                                return 48;

                            case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                                return 32;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                                return 66;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                                return 512;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                                return 768;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                                return 1024;

                            default:
                                throw new NotImplementedException();
                        }

                    default:
                        throw new NotImplementedException();
                }
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
                        switch ( SignatureType )
                        {
                            case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                                return 128;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                                return 64;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                                return 96;

                            case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                                return 32;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                                return 132;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                                return 256;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                                return 384;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                                return 512;

                            default:
                                throw new NotImplementedException();
                        }

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
                        switch ( SignatureType )
                        {
                            case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                                return 40;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                                return 64;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                                return 96;

                            case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                                return 64;

                            case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                                return 132;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA256_2048:
                                return 256;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA384_3072:
                                return 384;

                            case I2PSigningKey.SigningKeyTypes.RSA_SHA512_4096:
                                return 512;

                            default:
                                throw new NotImplementedException();
                        }

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

        public void Write( List<byte> dest )
        {
            Data.WriteTo( dest );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PCertificate" );
            result.AppendLine( "CType            : " + CType.ToString() );
            result.AppendLine( "SignatureType    : " + SignatureType.ToString() );

            if ( Payload != null )
            {
                result.AppendLine( "Payload          : " + Payload.Length + " bytes" );
            }
            else
            {
                result.AppendLine( "Payload          : (null)" );
            }

            if ( PayloadLength >= 4 )
            {
                result.AppendLine( "KEYSignatureType : " + KEYSignatureType.ToString() );
                result.AppendLine( "KEYPublicKeyType : " + KEYPublicKeyType.ToString() );
            }

            return result.ToString();
        }
    }
}
