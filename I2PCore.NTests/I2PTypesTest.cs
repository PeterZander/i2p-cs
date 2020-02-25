using NUnit.Framework;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PTests
{
    [TestFixture]
    public class I2PTypesTest
    {
        public I2PTypesTest()
        {
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
            //var asstr1 = dfromstr.ToBase64();
            Assert.IsTrue( BufUtils.Equal( destinfo.ToByteArray(), dfromstr.ToByteArray() ) );
        }
    }
}
