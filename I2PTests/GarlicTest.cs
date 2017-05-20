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
using I2PCore.Tunnel;

namespace I2PTests
{
    /// <summary>
    /// Summary description for GarlicTest
    /// </summary>
    [TestClass]
    public class GarlicTest
    {
        I2PPrivateKey Private;
        I2PPublicKey Public;
        I2PRouterIdentity Me;

        I2PPrivateKey DestinationPrivate;
        I2PPublicKey DestinationPublic;
        I2PRouterIdentity Destination;

        I2PSigningPrivateKey PrivateSigning;
        I2PSigningPublicKey PublicSigning;

        public GarlicTest()
        {
            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );

            Me = new I2PRouterIdentity( Public, new I2PSigningPublicKey( new BigInteger( "12" ), I2PKeyType.DefaultSigningKeyCert ) );

            DestinationPrivate = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            DestinationPublic = new I2PPublicKey( DestinationPrivate );
            Destination = new I2PRouterIdentity( DestinationPublic, new I2PSigningPublicKey( new BigInteger( "277626" ), I2PKeyType.DefaultSigningKeyCert ) );

            PrivateSigning = new I2PSigningPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigning = new I2PSigningPublicKey( PrivateSigning );
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
        public void TestGarlicCreate()
        {
            //var g = new Garlic( new GarlicCloveDeliveryTunnel( new DeliveryStatusMessage( 0x425c ), Destination.IdentHash, 1234 ) );
        }

        [TestMethod]
        public void TestGarlicCreateMessage()
        {
            var session = new DestinationSessions( ( d, h, inf ) => { }, () => null );

            /*
            var msg1 = session.CreateMessage( 
                Destination, 
                new GarlicCloveDeliveryTunnel( 
                    new DeliveryStatusMessage( 0x425c ), 
                    Destination.IdentHash, 
                    1234 ) );

            // Tags should be available now
            var msg2 = session.CreateMessage(
                Destination,
                new GarlicCloveDeliveryTunnel(
                    new DeliveryStatusMessage( 0x425c ),
                    Destination.IdentHash,
                    1234 ) );

            Assert.IsTrue( msg1.Garlic.Data != msg2.Garlic.Data );

            var other_session = new DestinationSessions( ( d, h, inf ) => { }, () => null );

            var msg3 = other_session.CreateMessage(
                Destination,
                new GarlicCloveDeliveryTunnel(
                    new DeliveryStatusMessage( 0x425c ),
                    Destination.IdentHash,
                    1234 ) );

            var msg4 = other_session.CreateMessage(
                Destination,
                new GarlicCloveDeliveryTunnel(
                    new DeliveryStatusMessage( 0x425c ),
                    Destination.IdentHash,
                    1234 ) );

            Assert.IsTrue( msg1.Garlic.Data != msg3.Garlic.Data );
            Assert.IsTrue( msg2.Garlic.Data != msg4.Garlic.Data );

            Assert.IsTrue( msg1.Garlic.Data != msg4.Garlic.Data );
            Assert.IsTrue( msg2.Garlic.Data != msg3.Garlic.Data );

            Assert.IsTrue( msg1.Garlic.Data.Length == msg3.Garlic.Data.Length );
            Assert.IsTrue( msg2.Garlic.Data.Length == msg4.Garlic.Data.Length );
             */
        }

        [TestMethod]
        public void TestEncodeDecode()
        {
            var recv = new ReceivedSessions( Private );
            var session = new DestinationSessions( ( d, h, inf ) => { }, () => null );

            var origmessage = new DeliveryStatusMessage( 0x425c );

            /*
            var msg1eg = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origmessage,
                    Destination.IdentHash,
                    1234 ) );

            var msg2aes = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origmessage,
                    Destination.IdentHash,
                    1234 ) );

            var dmsg1eg = recv.DecryptMessage( msg1eg.Garlic );
            var dmsg2aes = recv.DecryptMessage( msg2aes.Garlic );

            Assert.IsTrue( origmessage.Payload == dmsg1eg.Cloves.First().Message.Payload );
            Assert.IsTrue( origmessage.Payload == dmsg2aes.Cloves.First().Message.Payload );
             */
        }

        DatabaseStoreMessage CreateDatabaseStoreMessage()
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

            return dbsm;
        }

/*
 * The maximum size of an initial fragment is 956 bytes (assuming TUNNEL delivery mode); the 
 * maximum size of a follow-on fragment is 996 bytes. Therefore the maximum size is 
 * approximately 956 + (62 * 996) = 62708 bytes, or 61.2 KB.

 * In addition, the transports may have additional restrictions. 
 * NTCP currently limits to 16KB - 6 = 16378 bytes but this will be increased in a future release. 
 * The SSU limit is approximately 32 KB.

 * Note that these are not the limits for datagrams that the client sees, as the router may 
 * bundle a reply leaseset and/or session tags together with the client message in a garlic message. 
 * The leaseset and tags together may add about 5.5KB. Therefore the current datagram limit is about 10KB. 
 * This limit will be increased in a future release.
 * https://geti2p.net/en/docs/protocol/i2np
 */
        [TestMethod]
        public void TestBiggerEncodeDecode()
        {
            var recv = new ReceivedSessions( Private );
            var session = new DestinationSessions( ( d, h, inf ) => { }, () => null );

            var origdsmessage = new DeliveryStatusMessage( 0x425c );
            var datamessage = new DataMessage( new BufLen( BufUtils.Random( 16000 ) ) );
            var origmessage1 = CreateDatabaseStoreMessage();
            var origmessage2 = CreateDatabaseStoreMessage();
            var origmessage3 = CreateDatabaseStoreMessage();
            var origmessage4 = CreateDatabaseStoreMessage();

            /*
            var msg1eg = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origmessage1,
                    Destination.IdentHash,
                    1234 ),
                new GarlicCloveDeliveryLocal(
                    origmessage2 ),
                new GarlicCloveDeliveryRouter(
                    origmessage3,
                    Destination.IdentHash )
                );

            var msg2aes = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origdsmessage,
                    Destination.IdentHash,
                    1234 ),
                new GarlicCloveDeliveryRouter(
                    origmessage1,
                    Destination.IdentHash ),
                new GarlicCloveDeliveryLocal(
                    datamessage ),
                new GarlicCloveDeliveryTunnel(
                    origmessage4,
                    Destination.IdentHash,
                    1234 ) );

            var dmsg1eg = recv.DecryptMessage( msg1eg.Garlic );
            var dmsg2aes = recv.DecryptMessage( msg2aes.Garlic );

            Assert.IsTrue( dmsg1eg.Cloves.Count() == 3 );
            Assert.IsTrue( dmsg2aes.Cloves.Count() == 4 );

            Assert.IsTrue( dmsg2aes.Cloves[0].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Tunnel );
            Assert.IsTrue( dmsg2aes.Cloves[1].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Router );
            Assert.IsTrue( dmsg2aes.Cloves[2].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Local );
            Assert.IsTrue( dmsg2aes.Cloves[3].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Tunnel );

            Assert.IsTrue( dmsg1eg.Cloves.All( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ) );
            Assert.IsTrue( !dmsg2aes.Cloves.All( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ) );

            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DeliveryStatus ).
                All( m => ( (DeliveryStatusMessage)m.Message ).MessageId == 0x425c ) );

            Assert.IsTrue( dmsg1eg.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ).
                All( m => ( (DatabaseStoreMessage)m.Message ).RouterInfo.VerifySignature() ) );
            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ).
                All( m => ( (DatabaseStoreMessage)m.Message ).RouterInfo.VerifySignature() ) );
            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.Data ).
                All( m => ( (DataMessage)m.Message ).Payload == datamessage.Payload ) );
             */
        }
        
        [TestMethod]
        public void TestTooBigEncodeDecode()
        {
            var recv = new ReceivedSessions( Private );
            var session = new DestinationSessions( ( d, h, inf ) => { }, () => null );

            /*
            var datamessage = new DataMessage( new BufLen( BufUtils.Random( 65000 ) ) );

            try
            {
                var msg1eg = session.CreateMessage(
                    Me,
                    new GarlicCloveDeliveryTunnel(
                        datamessage,
                        Destination.IdentHash,
                        1234 )
                    );

                Assert.Fail();
            }
            catch ( Exception )
            {
                Assert.IsTrue( true );
            }
             */
        }
    }
}
