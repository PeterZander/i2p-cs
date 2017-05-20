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
using System.Net;
using I2PCore;

namespace I2PTests
{
    /// <summary>
    /// Summary description for GarlicTest
    /// </summary>
    [TestClass]
    public class I2PTypesTest
    {
        public I2PTypesTest()
        {
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

        [TestMethod]
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

        [TestMethod]
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
    }
}
