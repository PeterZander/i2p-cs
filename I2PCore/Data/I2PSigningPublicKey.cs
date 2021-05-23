using System;
using Org.BouncyCastle.Math;
using I2PCore.Utils;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Crypto.Parameters;

namespace I2PCore.Data
{
    public class I2PSigningPublicKey : I2PSigningKey
    {
        public override int KeySizeBytes { get { return Certificate.SigningPublicKeyLength; } }

        public I2PSigningPublicKey( BigInteger key, I2PCertificate cert ) : base( key, cert ) { }

        public I2PSigningPublicKey( I2PSigningPrivateKey privkey )
            : base( privkey.Certificate ) 
        {
            switch ( Certificate.SignatureType )
            {
                case SigningKeyTypes.DSA_SHA1:
                    Key = new BufLen( I2PConstants.DsaG.ModPow( privkey.ToBigInteger(), I2PConstants.DsaP ).ToByteArrayUnsigned() );
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    {
                        var param = NistNamedCurves.GetByName( "P-256" );
                        var domain = new ECDomainParameters( param.Curve, param.G, param.N, param.H );

                        var q = domain.G.Multiply( privkey.ToBigInteger() );
                        var publicparam = new ECPublicKeyParameters( q, domain );
                        Key = new BufLen( publicparam.Q.GetEncoded() );
                    }
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    {
                        var param = NistNamedCurves.GetByName( "P-384" );
                        var domain = new ECDomainParameters( param.Curve, param.G, param.N, param.H );

                        var q = domain.G.Multiply( privkey.ToBigInteger() );
                        var publicparam = new ECPublicKeyParameters( q, domain );
                        Key = new BufLen( publicparam.Q.GetEncoded() );
                    }
                    break;

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    {
                        var param = NistNamedCurves.GetByName( "P-521" );
                        var domain = new ECDomainParameters( param.Curve, param.G, param.N, param.H );

                        var q = domain.G.Multiply( privkey.ToBigInteger() );
                        var publicparam = new ECPublicKeyParameters( q, domain );
                        Key = new BufLen( publicparam.Q.GetEncoded() );
                    }
                    break;

                case SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    Key = new BufLen( new Ed25519PrivateKeyParameters( privkey.Key.BaseArray, privkey.Key.BaseArrayOffset ).GeneratePublicKey().GetEncoded() );
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        public I2PSigningPublicKey( BufRef buf, I2PCertificate cert ): base( cert )
        {
            Key = buf.ReadBufLen( KeySizeBytes );
        }
    }
}
