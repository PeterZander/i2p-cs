using NUnit.Framework;
using I2PCore.Data;
using I2PCore.Utils;
using System.Linq;
using System.Collections.Generic;

namespace I2PTests
{
    [TestFixture]
    public class I2PTypesTest
    {
        public I2PTypesTest()
        {
        }

        [Test]
        public void TestI2PDestination()
        {
            var certificate = new I2PCertificate( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );

            var keys = I2PPrivateKey.GetNewKeyPair();

            var privkey = keys.PrivateKey;
            var privskey = new I2PSigningPrivateKey( certificate );

            var dest = new I2PDestination(
                    keys.PublicKey,
                    new I2PSigningPublicKey( privskey ) );

            var d2 = new I2PDestination( new BufRefLen( dest.ToByteArray() ) );

            Assert.IsTrue( BufUtils.Equal( dest.ToByteArray(), d2.ToByteArray() ) );
        }

        [Test]
        public void TestI2PDestinationInfo()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );

            var asba = destinfo.ToByteArray();
            var dfromba = new I2PDestinationInfo( new BufRefLen( asba ) );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromba.ToByteArray() ) );

            var asstr = destinfo.ToBase64();
            var dfromstr = new I2PDestinationInfo( asstr );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromstr.ToByteArray() ) );
        }

        [Test]
        public void TestI2PDestinationInfo2()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.DSA_SHA1 );

            var asba = destinfo.ToByteArray();
            var dfromba = new I2PDestinationInfo( new BufRefLen( asba ) );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromba.ToByteArray() ) );

            var asstr = destinfo.ToBase64();
            var dfromstr = new I2PDestinationInfo( asstr );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromstr.ToByteArray() ) );
        }

        [Test]
        public void TestI2PDestinationInfo3()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.ECDSA_SHA384_P384 );

            var asba = destinfo.ToByteArray();
            var dfromba = new I2PDestinationInfo( new BufRefLen( asba ) );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromba.ToByteArray() ) );

            var asstr = destinfo.ToBase64();
            var dfromstr = new I2PDestinationInfo( asstr );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromstr.ToByteArray() ) );
        }

        [Test]
        public void TestI2PDestinationInfo4()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );

            var asba = destinfo.ToBase64();
            var dfromba = new I2PDestinationInfo( asba );
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromba.ToByteArray() ) );

            var asstr = destinfo.ToBase64();
            var dfromstr = new I2PDestinationInfo( asstr );

            var d1 = dfromba.Destination;
            var d2 = dfromstr.Destination;
            var d3 = dfromstr.Destination;

            Assert.IsTrue( BufUtils.Equal( d2.ToByteArray(), d2.ToByteArray() ) );

            Assert.IsTrue( d3.Padding == d2.Padding );
            Assert.IsTrue( d3.CertificateBuf == d2.CertificateBuf );
            Assert.IsTrue( d3.PublicKeyBuf == d2.PublicKeyBuf );
            Assert.IsTrue( d3.SigningPublicKeyBuf == d2.SigningPublicKeyBuf );
            Assert.IsTrue( BufUtils.Equal( d3.ToByteArray(), d2.ToByteArray() ) );

            Assert.IsTrue( d1.Padding == d2.Padding );
            Assert.IsTrue( d1.CertificateBuf == d2.CertificateBuf );
            Assert.IsTrue( d1.PublicKeyBuf == d2.PublicKeyBuf );
            Assert.IsTrue( d1.SigningPublicKeyBuf == d2.SigningPublicKeyBuf );
            Assert.IsTrue( BufUtils.Equal( d1.ToByteArray(), d2.ToByteArray() ) );

            Assert.IsTrue( d1.IdentHash == d2.IdentHash );
        }

        [Test]
        public void TestI2PLeaseSet()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            var dest = destinfo.Destination;

            var leases = Enumerable.Range( 1, 8 ).Select( i => new I2PLease( new I2PIdentHash( true ), new I2PTunnelId() ) );
            var ls = new I2PLeaseSet( dest, leases, dest.PublicKey, dest.SigningPublicKey, destinfo.PrivateSigningKey );

            Assert.IsTrue( ls.VerifySignature( dest.SigningPublicKey ) );

            var ls2 = new I2PLeaseSet( new BufRefLen( ls.ToByteArray() ) );
            Assert.IsTrue( ls2.VerifySignature( dest.SigningPublicKey ) );

            var ls3 = new I2PLeaseSet(
                    ls2.Destination, 
                    ls2.Leases.Select( l => 
                        new I2PLease( l.TunnelGw, l.TunnelId, new I2PDate( l.Expire ) ) ),
                    dest.PublicKey, dest.SigningPublicKey, destinfo.PrivateSigningKey );

            Assert.IsTrue( ls3.VerifySignature( dest.SigningPublicKey ) );

            Assert.IsTrue( new BufLen( ls.ToByteArray() ) == new BufLen( ls3.ToByteArray() ) );
        }
        [Test]
        public void TestI2PLeaseSet2()
        {
            var destinfo = new I2PDestinationInfo( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            var dest = destinfo.Destination;

            var leases = Enumerable.Range( 1, 8 ).Select( i => new I2PLease2( new I2PIdentHash( true ), new I2PTunnelId() ) );
            var ls = new I2PLeaseSet2( dest, leases, new I2PPublicKey[] { dest.PublicKey }, dest.SigningPublicKey, destinfo.PrivateSigningKey );

            //Assert.IsTrue( ls.VerifySignature( dest.SigningPublicKey ) );

            var ls2 = new I2PLeaseSet2( new BufRefLen( ls.ToByteArray() ) );
            //Assert.IsTrue( ls2.VerifySignature( dest.SigningPublicKey ) );

            var ls3 = new I2PLeaseSet2(
                    ls2.Destination, 
                    ls2.Leases.Select( l => 
                        new I2PLease2( l.TunnelGw, l.TunnelId, new I2PDateShort( l.Expire ) ) ),
                    new List<I2PPublicKey>( ls2.PublicKeys ), dest.SigningPublicKey, destinfo.PrivateSigningKey );

            //Assert.IsTrue( ls3.VerifySignature( dest.SigningPublicKey ) );

            Assert.IsTrue( new BufLen( ls.ToByteArray() ) == new BufLen( ls3.ToByteArray() ) );
        }
    }
}
