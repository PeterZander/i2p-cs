using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Utils;
using Chaos.NaCl;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Crypto;

namespace I2PCore.Data
{
    public class I2PSignature : I2PType
    {
        public BufLen Sig;

        public I2PCertificate Certificate;

        public I2PSignature()
        {
            Certificate = I2PSigningKey.DefaultSigningKeyCert;
            Sig = new BufLen( new byte[Certificate.SignatureLength] );
        }

        public I2PSignature( BufRef buf, I2PCertificate cert )
        {
            Certificate = cert;

            Sig = buf.ReadBufLen( cert.SignatureLength );
        }

        // TODO: Add more signature types
        public static bool SupportedSignatureType( I2PSigningKey.SigningKeyTypes stype )
        {
            return stype == I2PSigningKey.SigningKeyTypes.DSA_SHA1 
                || stype == I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256
                || stype == I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384
                || stype == I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521
                || stype == I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519;
        }

        public static byte[] DoSign( I2PSigningPrivateKey key, params BufLen[] bufs )
        {
            //Logging.LogDebug( "DoSign: " + key.Certificate.SignatureType.ToString() );

            switch ( key.Certificate.SignatureType )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    return DoSignDsaSha1( bufs, key );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    return DoSignEcDsa( bufs, key, new Sha256Digest(), NistNamedCurves.GetByName( "P-256" ), 64 );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    return DoSignEcDsa( bufs, key, new Sha384Digest(), NistNamedCurves.GetByName( "P-384" ), 96 );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    return DoSignEcDsa( bufs, key, new Sha512Digest(), NistNamedCurves.GetByName( "P-521" ), 132 );

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    return DoSignEdDSASHA512Ed25519( bufs, key );

                default:
                    throw new NotImplementedException();
            }
        }

        public static byte[] DoSignEdDSASHA512Ed25519( IEnumerable<BufLen> bufs, I2PSigningPrivateKey key )
        {
            return Chaos.NaCl.Ed25519.Sign( bufs.SelectMany( b => b.ToByteArray() ).ToArray(), key.ExpandedPrivateKey );
        }

        public static byte[] DoSignDsaSha1( IEnumerable<BufLen> bufs, I2PSigningPrivateKey key )
        {
            var sha = new Sha1Digest();
            foreach( var buf in bufs ) sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );

            var s = new Org.BouncyCastle.Crypto.Signers.DsaSigner();

            var dsaparams = new ParametersWithRandom(
                new DsaPrivateKeyParameters(
                    key.ToBigInteger(),
                    new DsaParameters(
                        I2PConstants.DsaP,
                        I2PConstants.DsaQ,
                        I2PConstants.DsaG ) ) );

            s.Init( true, dsaparams );
            var sig = s.GenerateSignature( hash );
            var result = new byte[40];

            var b1 = sig[0].ToByteArrayUnsigned();
            var b2 = sig[1].ToByteArrayUnsigned();

            // https://geti2p.net/en/docs/spec/common-structures#type_Signature
            // When a signature is composed of two elements (for example values R,S), 
            // it is serialized by padding each element to length/2 with leading zeros if necessary.
            // All types are Big Endian, except for EdDSA, which is stored and transmitted in a Little Endian format.

            // Pad msb. Big endian.
            Array.Copy( b1, 0, result, 0 + 20 - b1.Length, b1.Length );
            Array.Copy( b2, 0, result, 20 + 20 - b2.Length, b2.Length );

            return result;
        }

        public static byte[] DoSignEcDsaSha256P256_old( IEnumerable<BufLen> bufs, I2PSigningPrivateKey key )
        {
            var sha = new Sha256Digest();
            foreach ( var buf in bufs ) sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );

            var p = Org.BouncyCastle.Asn1.Nist.NistNamedCurves.GetByName( "P-256" );
            var param = new ECDomainParameters( p.Curve, p.G, p.N, p.H );
            var pk = new ECPrivateKeyParameters( key.ToBigInteger(), param );

            var s = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner();
            s.Init( true, new ParametersWithRandom( pk ) );

            var sig = s.GenerateSignature( hash );
            var result = new byte[64];

            var b1 = sig[0].ToByteArrayUnsigned();
            var b2 = sig[1].ToByteArrayUnsigned();

            // https://geti2p.net/en/docs/spec/common-structures#type_Signature
            // When a signature is composed of two elements (for example values R,S), 
            // it is serialized by padding each element to length/2 with leading zeros if necessary.
            // All types are Big Endian, except for EdDSA, which is stored and transmitted in a Little Endian format.

            // Pad msb. Big endian.
            Array.Copy( b1, 0, result, 0 + 20 - b1.Length, b1.Length );
            Array.Copy( b2, 0, result, 20 + 20 - b2.Length, b2.Length );

            Logging.LogDebug( "DoSignEcDsaSha256P256: Used." );

            return result;
        }

        public static byte[] DoSignEcDsa( 
            IEnumerable<BufLen> bufs, 
            I2PSigningPrivateKey key, 
            IDigest digest, 
            X9ECParameters ecparam, 
            int sigsize )
        {
            foreach ( var buf in bufs ) digest.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[digest.GetDigestSize()];
            digest.DoFinal( hash, 0 );

            var param = new ECDomainParameters( ecparam.Curve, ecparam.G, ecparam.N, ecparam.H );
            var pk = new ECPrivateKeyParameters( key.ToBigInteger(), param );

            var s = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner();
            s.Init( true, new ParametersWithRandom( pk ) );

            var sig = s.GenerateSignature( hash );
            var result = new byte[sigsize];

            var b1 = sig[0].ToByteArrayUnsigned();
            var b2 = sig[1].ToByteArrayUnsigned();

            // https://geti2p.net/en/docs/spec/common-structures#type_Signature
            // When a signature is composed of two elements (for example values R,S), 
            // it is serialized by padding each element to length/2 with leading zeros if necessary.
            // All types are Big Endian, except for EdDSA, which is stored and transmitted in a Little Endian format.

            // Pad msb. Big endian.
            Array.Copy( b1, 0, result, sigsize / 2 - b1.Length, b1.Length );
            Array.Copy( b2, 0, result, sigsize - b2.Length, b2.Length );

            Logging.LogDebug( "DoSignEcDsa: " + digest.ToString() + ": Used." );

            return result;
        }

        public static bool DoVerify( I2PSigningPublicKey key, I2PSignature signed, params BufLen[] bufs )
        {
            //Logging.LogDebug( "DoVerify: " + key.Certificate.SignatureType.ToString() );

            switch ( key.Certificate.SignatureType )
            {
                case I2PSigningKey.SigningKeyTypes.DSA_SHA1:
                    return DoVerifyDsaSha1( bufs, key, signed );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256:
                    return DoVerifyEcDsa( bufs, key, signed, new Sha256Digest(), NistNamedCurves.GetByName( "P-256" ) );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384:
                    return DoVerifyEcDsa( bufs, key, signed, new Sha384Digest(), NistNamedCurves.GetByName( "P-384" ) );

                case I2PSigningKey.SigningKeyTypes.ECDSA_SHA512_P521:
                    return DoVerifyEcDsa( bufs, key, signed, new Sha512Digest(), NistNamedCurves.GetByName( "P-521" ) );

                case I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519:
                    return DoVerifyEdDSASHA512Ed25519( bufs, key, signed );

                default:
                    throw new NotImplementedException();
            }
        }

        public static bool DoVerifyEdDSASHA512Ed25519( IEnumerable<BufLen> bufs, I2PSigningPublicKey key, I2PSignature signed )
        {
            return Chaos.NaCl.Ed25519.Verify( 
                        signed.Sig.ToByteArray(), 
                        bufs.SelectMany( b => 
                                b
                                    .ToByteArray() )
                                    .ToArray(),
                        key.ToByteArray() );
        }

        public static bool DoVerifyDsaSha1( IEnumerable<BufLen> bufs, I2PSigningPublicKey key, I2PSignature signed )
        {
            if ( !SupportedSignatureType( signed.Certificate.SignatureType ) ) throw new NotImplementedException();

            var sha = new Sha1Digest();
            foreach ( var buf in bufs ) sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );

            var dsa = new Org.BouncyCastle.Crypto.Signers.DsaSigner();

            var sigsize = signed.Certificate.SignatureLength;
            var r = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset + 0, sigsize / 2 );
            var s = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset + sigsize / 2, sigsize / 2 );

            var dsaparams = 
                new DsaPublicKeyParameters(
                    key.ToBigInteger(),
                    new DsaParameters(
                        I2PConstants.DsaP,
                        I2PConstants.DsaQ,
                        I2PConstants.DsaG ) );

            dsa.Init( false, dsaparams );
            return dsa.VerifySignature( hash, r, s );
        }

        public static bool DoVerifyEcDsaSha256P256_old( IEnumerable<BufLen> bufs, I2PSigningPublicKey key, I2PSignature signed )
        {
            if ( !SupportedSignatureType( signed.Certificate.SignatureType ) ) throw new NotImplementedException();

            var sha = new Sha256Digest();
            foreach ( var buf in bufs ) sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );

            var p = Org.BouncyCastle.Asn1.Nist.NistNamedCurves.GetByName( "P-256" );
            var param = new ECDomainParameters( p.Curve, p.G, p.N, p.H );
            var pk = new ECPublicKeyParameters( p.Curve.DecodePoint( key.ToByteArray() ), param );

            var dsa = new Org.BouncyCastle.Crypto.Signers.DsaSigner();

            var sigsize = signed.Certificate.SignatureLength;
            var r = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset + 0, sigsize / 2 );
            var s = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset + sigsize / 2, sigsize / 2 );

            dsa.Init( false, pk ); 
            var result = dsa.VerifySignature( hash, r, s );
            Logging.LogDebug( "DoVerifyEcDsaSha256P256: " + result.ToString() );
            return result;
        }

        public static bool DoVerifyEcDsa( 
            IEnumerable<BufLen> bufs, 
            I2PSigningPublicKey key, 
            I2PSignature signed,
            IDigest digest,
            X9ECParameters ecparam )
        {
            if ( !SupportedSignatureType( signed.Certificate.SignatureType ) ) throw new NotImplementedException();

            foreach ( var buf in bufs ) digest.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[digest.GetDigestSize()];
            digest.DoFinal( hash, 0 );

            var param = new ECDomainParameters( ecparam.Curve, ecparam.G, ecparam.N, ecparam.H );
            var pk = new ECPublicKeyParameters( ecparam.Curve.DecodePoint( key.ToByteArray() ), param );

            var dsa = new Org.BouncyCastle.Crypto.Signers.DsaSigner();

            var sigsize = signed.Certificate.SignatureLength;
            var r = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset, sigsize / 2 );
            var s = new BigInteger( 1, signed.Sig.BaseArray, signed.Sig.BaseArrayOffset + sigsize / 2, sigsize / 2 );

            dsa.Init( false, pk );
            var result = dsa.VerifySignature( hash, r, s );
            Logging.LogDebug( "DoVerifyEcDsa: " + result.ToString() + ": " + digest.ToString() );
            return result;
        }

        public void Write( BufRefStream dest )
        {
            Sig.WriteTo( dest );
        }

        public override string ToString()
        {
            return "I2PSignature: " + FreenetBase64.Encode( Sig );
        }
    }
}
