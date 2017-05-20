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
using I2PCore.Tunnel;
using System.Net;

namespace I2PTests
{
    /// <summary>
    /// Summary description for GarlicTest
    /// </summary>
    [TestClass]
    public class DatabaseStoreMessageTest
    {
        I2PPrivateKey Private;
        I2PPublicKey Public;
        I2PSigningPrivateKey PrivateSigning;
        I2PSigningPublicKey PublicSigning;
        I2PSigningPrivateKey PrivateSigningEd25519;
        I2PSigningPublicKey PublicSigningEd25519;
        I2PRouterIdentity Me;

        public DatabaseStoreMessageTest()
        {
            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );
            PrivateSigning = new I2PSigningPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigning = new I2PSigningPublicKey( PrivateSigning );

            var CertificateEd25519 = new I2PCertificate( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            PrivateSigningEd25519 = new I2PSigningPrivateKey( CertificateEd25519 );
            PublicSigningEd25519 = new I2PSigningPublicKey( PrivateSigningEd25519 );

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
        public void TestSimpleDatabaseStoreCreation()
        {
            var mapping = new I2PMapping();
            mapping["One"] = "1";
            mapping["2"] = "Two";

            var ri = new I2PRouterInfo(
                new I2PRouterIdentity( Public, PublicSigning ),
                I2PDate.Now,
                new I2PRouterAddress[] { new I2PRouterAddress( new IPAddress( 424242L ), 773, 42, "SSU" ) },
                mapping, 
                PrivateSigning );

            var dbsm = new DatabaseStoreMessage( ri );

            var data = dbsm.Header16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.RouterInfo.Options["One"] == "1" );
            Assert.IsTrue( rdsm.RouterInfo.Options["2"] == "Two" );
            Assert.IsTrue( rdsm.RouterInfo.VerifySignature() );
        }

        [TestMethod]
        public void TestSimpleDatabaseHeader5StoreCreation()
        {
            var mapping = new I2PMapping();
            mapping["One"] = "1";
            mapping["2"] = "Two";

            var ri = new I2PRouterInfo(
                new I2PRouterIdentity( Public, PublicSigning ),
                I2PDate.Now,
                new I2PRouterAddress[] { new I2PRouterAddress( new IPAddress( 424242L ), 773, 42, "SSU" ) },
                mapping,
                PrivateSigning );

            var dbsm = new DatabaseStoreMessage( ri );

            var data = dbsm.Header16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.RouterInfo.Options["One"] == "1" );
            Assert.IsTrue( rdsm.RouterInfo.Options["2"] == "Two" );
            Assert.IsTrue( rdsm.RouterInfo.VerifySignature() );
        }

        [TestMethod]
        public void TestSimpleDatabaseStoreLeaseSetCreation()
        {
            var linfo = new I2PLeaseInfo( Public, PublicSigning, Private, PrivateSigning );
            var leases = new List<I2PLease>();
            for ( int i = 0; i < 5; ++i ) leases.Add( new I2PLease( new I2PIdentHash( true ), (uint)( ( i * 72 + 6 ) * i * 1314 + 5 ) % 40000, I2PDate.Now ) );
            var ls = new I2PLeaseSet( new I2PDestination( Public, PublicSigning ), leases, linfo );

            var dbsm = new DatabaseStoreMessage( ls );

            var data = dbsm.Header16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.LeaseSet.Leases.Count == 5 );

            Assert.IsTrue( BufUtils.Equal( ls.Destination.ToByteArray(), rdsm.LeaseSet.Destination.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicKey.ToByteArray(), rdsm.LeaseSet.PublicKey.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicSigningKey.ToByteArray(), rdsm.LeaseSet.PublicSigningKey.ToByteArray() ) );
            for ( int i = 0; i < 5; ++i ) 
                Assert.IsTrue( BufUtils.Equal( ls.Leases[i].ToByteArray(), rdsm.LeaseSet.Leases[i].ToByteArray() ) );

            Assert.IsTrue( rdsm.LeaseSet.VerifySignature() );
        }

        [TestMethod]
        public void TestSimpleDatabaseStoreLeaseSetEd25519Creation()
        {
            var linfo = new I2PLeaseInfo( Public, PublicSigningEd25519, Private, PrivateSigningEd25519 );
            var leases = new List<I2PLease>();
            for ( int i = 0; i < 5; ++i ) leases.Add( new I2PLease( new I2PIdentHash( true ), (uint)( ( i * 72 + 6 ) * i * 1314 + 5 ) % 40000, I2PDate.Now ) );
            var ls = new I2PLeaseSet( new I2PDestination( Public, PublicSigningEd25519 ), leases, linfo );

            var dbsm = new DatabaseStoreMessage( ls );

            var data = dbsm.Header16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.LeaseSet.Leases.Count == 5 );

            Assert.IsTrue( BufUtils.Equal( ls.Destination.ToByteArray(), rdsm.LeaseSet.Destination.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicKey.ToByteArray(), rdsm.LeaseSet.PublicKey.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicSigningKey.ToByteArray(), rdsm.LeaseSet.PublicSigningKey.ToByteArray() ) );
            for ( int i = 0; i < 5; ++i )
                Assert.IsTrue( BufUtils.Equal( ls.Leases[i].ToByteArray(), rdsm.LeaseSet.Leases[i].ToByteArray() ) );

            Assert.IsTrue( rdsm.LeaseSet.VerifySignature() );
        }
    }
}
