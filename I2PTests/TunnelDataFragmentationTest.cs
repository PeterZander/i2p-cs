using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Data;
using I2PCore.Tunnel;
using I2PCore.Utils;

namespace I2PTests
{
    /// <summary>
    /// Summary description for TunnelDataFragmentationTest
    /// </summary>
    [TestClass]
    public class TunnelDataFragmentationTest
    {
        public TunnelDataFragmentationTest()
        {
            //
            // TODO: Add constructor logic here
            //
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
        public void MakeAndReadFragments()
        {
            var origmsgs = new List<TunnelMessage>();
            
            for ( int i = 0; i < 200; ++i )
            {
                switch( BufUtils.RandomInt( 3 ) )
                {
                    case 0:
                        var adatarec = new DataMessage( new BufLen( BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageTunnel( 
                            adatarec.Header16, 
                            new I2PIdentHash( true ), 
                            BufUtils.RandomUint() ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage( 
                            new I2PIdentHash( true ), 
                            new I2PIdentHash( true ), 
                            DatabaseLookupMessage.LookupTypes.Normal );

                        origmsgs.Add( new TunnelMessageRouter( arec.Header16, new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage( new BufLen( BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec2.Header16 ) );
                        break;
                }
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( msgs );

            foreach( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m =>
                    m.Delivery == rmsg.Delivery &&
                    m.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload
                    ) != null );
            }
        }
        
        [TestMethod]
        public void MakeAndReadFragmentsWithSerialize()
        {
            var origmsgs = new List<TunnelMessage>();

            for ( int i = 0; i < 200; ++i )
            {
                switch ( BufUtils.RandomInt( 3 ) )
                {
                    case 0:
                        var adatarec = new DataMessage( new BufLen( BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec.Header16 ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage(
                            new I2PIdentHash( true ),
                            new I2PIdentHash( true ),
                            DatabaseLookupMessage.LookupTypes.RouterInfo );

                        origmsgs.Add( new TunnelMessageRouter( arec.Header16, new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage( new BufLen( BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageTunnel( adatarec2.Header16,
                            new I2PIdentHash( true ),
                            BufUtils.RandomUint() ) );
                        break;
                }
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );
            var recvlist = new List<TunnelDataMessage>();

            foreach ( var msg in msgs )
            {
                recvlist.Add( (TunnelDataMessage)I2NPMessage.ReadHeader16( new BufRefLen( msg.Header16.HeaderAndPayload ) ).Message );
            }

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( recvlist );

            foreach ( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m => 
                    m.Delivery == rmsg.Delivery &&
                    m.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload 
                    ) != null );
            }
        }
    }
}
