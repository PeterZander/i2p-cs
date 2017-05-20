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
    public class UtilsTest
    {
        public UtilsTest()
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
        public void TestTickCounter()
        {
            var start = new TickCounter();

            Assert.IsTrue( TickCounter.MaxDelta.DeltaToNowMilliseconds > 0 );

            var maxd = TickCounter.MaxDelta;
            System.Threading.Thread.Sleep( 200 );
            Assert.IsTrue( maxd.DeltaToNowMilliseconds > 0 );
            Assert.IsTrue( maxd.DeltaToNowMilliseconds > int.MaxValue / 2 );

            Assert.IsTrue( start.DeltaToNowMilliseconds > 0 );

            Assert.IsTrue( Math.Round( ( TickCounter.Now - start ).ToSeconds / 3f ) == Math.Round( (float)start.DeltaToNowSeconds / 3f ) );

            var start_copy = new TickCounter( start.Ticks );

            System.Threading.Thread.Sleep( BufUtils.RandomInt( 300 ) + 200 );

            var startdelta = start.DeltaToNowMilliseconds;
            var now1 = new TickCounter();

            Assert.IsTrue( start.ToString().Length > 0 );

            Assert.IsTrue( ( now1 - start ).ToMilliseconds > 0 );
            Assert.IsTrue( ( ( now1 - start ).ToMilliseconds ) / 100 == startdelta / 100 );
        }

        [TestMethod]
        public void TestGZip()
        {
            var smalldata = BufUtils.Random( 200 );
            var bigdata = BufUtils.Random( 2 * 1024 * 1024 );

            var smalldata_zero = new byte[200];
            var bigdata_zero = new byte[2 * 1024 * 1024];

            var b1 = LZUtils.BCGZipCompressNew( new BufLen( smalldata ) );
            var b2 = LZUtils.BCGZipDecompressNew( b1 );
            Assert.IsTrue( b2 == new BufLen( smalldata ) );

            b1 = LZUtils.BCGZipCompressNew( new BufLen( bigdata ) );
            b2 = LZUtils.BCGZipDecompressNew( b1 );
            Assert.IsTrue( b2 == new BufLen( bigdata ) );

            b1 = LZUtils.BCGZipCompressNew( new BufLen( smalldata_zero ) );
            b2 = LZUtils.BCGZipDecompressNew( b1 );
            Assert.IsTrue( b2 == new BufLen( smalldata_zero ) );

            b1 = LZUtils.BCGZipCompressNew( new BufLen( bigdata_zero ) );
            b2 = LZUtils.BCGZipDecompressNew( b1 );
            Assert.IsTrue( b2 == new BufLen( bigdata_zero ) );

            var ba1 = LZUtils.BCGZipCompress( bigdata );
            b2 = LZUtils.BCGZipDecompressNew( new BufLen( ba1 ) );
            Assert.IsTrue( b2 == new BufLen( bigdata ) );

            b1 = LZUtils.BCGZipCompressNew( new BufLen( bigdata_zero ) );
            var ba2 = LZUtils.BCGZipDecompress( b1 );
            Assert.IsTrue( new BufLen( ba2 ) == new BufLen( bigdata_zero ) );

            for ( int i = bigdata.Length / 10; i < bigdata.Length - bigdata.Length / 10; ++i ) bigdata[i] = 42;
            b1 = LZUtils.BCGZipCompressNew( new BufLen( bigdata ) );
            b2 = LZUtils.BCGZipDecompressNew( b1 );
            Assert.IsTrue( b2 == new BufLen( bigdata ) );
        }

        [TestMethod]
        public void TestRoulette()
        {
            var l = BufUtils.Random( 10000 ).AsEnumerable();
            var r = new I2PCore.Utils.RouletteSelection<byte,byte>( l, v => v, k => k == 42 ? 30f : 1f, float.MaxValue );

            int is42 = 0;
            int samples = 10000;
            for ( int i = 0; i < samples; ++i )
            {
                if ( r.GetWeightedRandom() == 42 ) ++is42;
            }

            Assert.IsTrue( is42 > ( 20 * samples ) / 256 );
        }

        [TestMethod]
        public void TestRoulette2()
        {
            var l = BufUtils.Random( 10000 ).AsEnumerable();
            l = l.Concat( BufUtils.Populate<byte>( 42, 10000 ) );
            var r = new I2PCore.Utils.RouletteSelection<byte, byte>( l, v => v, k => 1f, float.MaxValue );

            int is42 = 0;
            int samples = 10000;
            for ( int i = 0; i < samples; ++i )
            {
                if ( r.GetWeightedRandom() == 42 ) ++is42;
            }

            Assert.IsTrue( is42 > samples / 2 - samples / 30 );
        }

        [TestMethod]
        public void TestRoulette3()
        {
            var l = BufUtils.Populate<float>( () => BufUtils.RandomInt( 2 ) == 0 ? 0f : BufUtils.RandomFloat( 100000 ), 10000 );
            var r = new I2PCore.Utils.RouletteSelection<float, float>( l, v => v, k => k, 2f );

            int iszero = 0;
            for ( int i = 0; i < 10000; ++i )
            {
                if ( r.GetWeightedRandom() == 0f ) ++iszero;
            }

            Assert.IsTrue( iszero > 10000 / 5 );
            Assert.IsTrue( iszero < 10000 / 2 );

            r = new I2PCore.Utils.RouletteSelection<float, float>( l, v => v, k => k, float.MaxValue );

            iszero = 0;
            for ( int i = 0; i < 10000; ++i )
            {
                if ( r.GetWeightedRandom() == 0f ) ++iszero;
            }

            Assert.IsTrue( iszero < 4 );
        }

        [TestMethod]
        public void TestBase32()
        {
            /* Test vectors from RFC 4648 */
            /*
            Assert.IsTrue( TestBase32Enc( "", "" ) );
            Assert.IsTrue( TestBase32Enc( "f", "MY======" ) );
            Assert.IsTrue( TestBase32Enc( "fo", "MZXQ====" ) );
            Assert.IsTrue( TestBase32Enc( "foo", "MZXW6===" ) );
            Assert.IsTrue( TestBase32Enc( "foob", "MZXW6YQ=" ) );
            Assert.IsTrue( TestBase32Enc( "fooba", "MZXW6YTB" ) );
            Assert.IsTrue( TestBase32Enc( "foobar", "MZXW6YTBOI======" ) );
             */
            Assert.IsTrue( TestBase32Enc( "", "" ) );
            Assert.IsTrue( TestBase32Enc( "f", "MY" ) );
            Assert.IsTrue( TestBase32Enc( "fo", "MZXQ" ) );
            Assert.IsTrue( TestBase32Enc( "foo", "MZXW6" ) );
            Assert.IsTrue( TestBase32Enc( "foob", "MZXW6YQ" ) );
            Assert.IsTrue( TestBase32Enc( "fooba", "MZXW6YTB" ) );
            Assert.IsTrue( TestBase32Enc( "foobar", "MZXW6YTBOI" ) );
        }

        bool TestBase32Enc( string src, string expected )
        {
            var enc = Encoding.ASCII.GetBytes( src );
            var encb32 = BufUtils.ToBase32String( enc );
            return encb32 == expected.ToLower();
        }
    }
}
