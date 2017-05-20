using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using Org.BouncyCastle.Math;

namespace I2PTests
{
    /// <summary>
    /// Summary description for ElGamalTest
    /// </summary>
    [TestClass]
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

        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        [TestMethod]
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
