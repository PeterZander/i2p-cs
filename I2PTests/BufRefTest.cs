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

namespace I2PTests
{
    /// <summary>
    /// Summary description for GarlicTest
    /// </summary>
    [TestClass]
    public class BufRefTest
    {
        public BufRefTest()
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
        public void TestBufLen()
        {
            BufLen b1null = null;
            var b1 = new BufLen( BufUtils.Random( 70 ) );
            var b2 = new BufLen( BufUtils.Random( 70 ) );
            b1[0] = 2;
            b2[0] = 1;

#pragma warning disable 1718
            Assert.IsTrue( b1null == b1null );
            Assert.IsTrue( b1null == null );
            Assert.IsTrue( b1 != b1null );
            Assert.IsTrue( b1 != null );
            Assert.IsTrue( null != b1 );
            Assert.IsTrue( b1 == b1 );
            Assert.IsTrue( b1 != b2 );
            Assert.IsTrue( b1 == b1.Clone() );
            Assert.IsTrue( b1.AsEnumerable().Average( b => b ) == b1.Clone().AsEnumerable().Average( b => b ) );
            Assert.IsTrue( ( (IComparable<BufLen>)b1 ).CompareTo( b1.Clone() ) == 0 );
#pragma warning restore 1718

            var list = new List<byte>( b1 );
            Assert.IsTrue( b1 == new BufLen( list.ToArray() ) );

            var buf = new byte[5];
            b2.Peek( buf, 30 );
            Assert.IsTrue( ( new BufLen( b2, 30, 5 ) ) == new BufLen( buf ) );

            Assert.IsTrue( b1 != b2 );
            Assert.IsTrue( ( (IComparable<BufLen>)b1 ).CompareTo( b2 ) > 0 );
            Assert.IsTrue( b1 > b2 );
            Assert.IsFalse( b1 < b2 );
            Assert.IsTrue( b1 > new BufLen( b1, 0, 20 ) );
            Assert.IsFalse( b1 < new BufLen( b1, 0, 20 ) );
            Assert.IsTrue( new BufLen( b1, 0, 20 ) < b1 );
            Assert.IsFalse( new BufLen( b1, 0, 20 ) > b1 );

            Assert.IsTrue( b1.GetHashCode() == b1.GetHashCode() );
            Assert.IsTrue( b1.GetHashCode() == b1.Clone().GetHashCode() );

            b1.PokeFlip32( 0x326de4f7, 20 );
            Assert.IsTrue( b1[20] == 0x32 );
            Assert.IsTrue( b1[21] == 0x6d );
            Assert.IsTrue( b1[22] == 0xe4 );
            Assert.IsTrue( b1[23] == 0xf7 );

            b1.Poke32( 0x326de4f7, 21 );
            Assert.IsTrue( b1[20] == 0x32 );
            Assert.IsTrue( b1[21] == 0xf7 );
            Assert.IsTrue( b1[22] == 0xe4 );
            Assert.IsTrue( b1[23] == 0x6d );
            Assert.IsTrue( b1[24] == 0x32 );
        }

        [TestMethod]
        public void TestBufLen2()
        {
            var b1 = new BufLen( new byte[] { 0x53, 0x66, 0xF3 } );
            var b2 = new BufLen( new byte[] { 0x53, 0x62, 0xF3 } );
            var b3 = new BufLen( new byte[] { 0x53, 0x66, 0xF5 } );

            Assert.IsTrue( b1 > b2 );
            Assert.IsTrue( b1 < b3 );
            Assert.IsTrue( b2 < b3 );

            b1 = new BufLen( new byte[] { 0x67, 0x53, 0x66, 0xF3 }, 1 );
            b2 = new BufLen( new byte[] { 0xF7, 0x53, 0x62, 0xF3 }, 1 );
            b3 = new BufLen( new byte[] { 0x07, 0x53, 0x66, 0xF5 }, 1 );

            Assert.IsTrue( b1 > b2 );
            Assert.IsTrue( b1 < b3 );
            Assert.IsTrue( b2 < b3 );
        }
    }
}