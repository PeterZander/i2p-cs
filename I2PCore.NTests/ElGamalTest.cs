using NUnit.Framework;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Math;

namespace I2PTests
{
    [TestFixture]
    public class ElGamalTest
    {
        I2PPrivateKey Private;
        I2PPublicKey Public;
        I2PRouterIdentity Me;

        public ElGamalTest()
        {
            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );

            Me = new I2PRouterIdentity( Public, new I2PSigningPublicKey( new BigInteger( "12" ), I2PKeyType.DefaultSigningKeyCert ) );
        }

        [Test]
        public void TestElGamal()
        {
            for ( int i = 0; i < 20; ++i )
            {
                var egdata = new BufLen( new byte[514] );
                var writer = new BufRefLen( egdata );
                var data = new BufLen( egdata, 0, 222 );

                data.Randomize();
                var origdata = data.Clone();

                var eg = new ElGamalCrypto( Public );
                eg.Encrypt( writer, data, true );

                var decryptdata = ElGamalCrypto.Decrypt( egdata, Private, true );

                Assert.IsTrue( decryptdata == origdata );
            }
        }
    }
}
