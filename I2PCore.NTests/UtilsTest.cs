using System;
using System.Text;
using System.Linq;
using NUnit.Framework;
using I2PCore.Utils;

namespace I2PTests
{
    [TestFixture]
    public class UtilsTest
    {
        public UtilsTest()
        {
        }

        [Test]
        public void TestTickCounter()
        {
            var start = new TickCounter();

            Assert.IsTrue( TickCounter.MaxDelta.DeltaToNowMilliseconds > 0 );
            Assert.IsTrue( TickCounter.MaxDelta.DeltaToNow > TickSpan.Milliseconds( 0 ) );

            var maxd = TickCounter.MaxDelta;
            System.Threading.Thread.Sleep( 200 );

            Assert.IsTrue( maxd.DeltaToNowMilliseconds > 0 );
            Assert.IsTrue( maxd.DeltaToNowMilliseconds > int.MaxValue / 2 );

            Assert.IsTrue( maxd.DeltaToNow > TickSpan.Milliseconds( 0 ) );
            Assert.IsTrue( maxd.DeltaToNow > TickSpan.Milliseconds( int.MaxValue / 2 ) );

            Assert.IsTrue( start.DeltaToNowMilliseconds > 0 );
            Assert.IsTrue( start.DeltaToNow > TickSpan.Milliseconds( 0 ) );

            Assert.IsTrue( (int)Math.Round( ( TickCounter.Now - start ).ToSeconds / 3f ) 
                    == (int)Math.Round( start.DeltaToNowSeconds / 3f ) );

            Assert.IsTrue( (int)Math.Round( ( ( TickCounter.Now - start ) / 3f ).ToSeconds )
                    == (int)Math.Round( ( start.DeltaToNow / 3f ).ToSeconds ) );

            var start_copy = new TickCounter( start.Ticks );

            System.Threading.Thread.Sleep( BufUtils.RandomInt( 300 ) + 200 );

            var startdelta = start.DeltaToNowMilliseconds;
            var startdeltaspan = start.DeltaToNow;
            var now1 = new TickCounter();

            Assert.IsTrue( start.ToString().Length > 0 );

            Assert.IsTrue( ( now1 - start ).ToMilliseconds > 0 );
            Assert.IsTrue( ( ( now1 - start ).ToMilliseconds ) / 100 == startdelta / 100 );

            Assert.IsTrue( now1 - start > TickSpan.Milliseconds( 0 ) );
            Assert.IsTrue( ( now1 - start ) / 100 == startdeltaspan / 100 );
        }

        [Test]
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

        [Test]
        public void TestRoulette()
        {
            var l = BufUtils.Random( 10000 ).AsEnumerable();
            var r = new I2PCore.Utils.RouletteSelection<byte,byte>( l, v => v, k => k == 42 ? 30f : 1f );

            int is42 = 0;
            int samples = 10000;
            for ( int i = 0; i < samples; ++i )
            {
                if ( r.GetWeightedRandom( null ) == 42 ) ++is42;
            }

            Assert.IsTrue( is42 > ( 3 * samples ) / 256 );
        }

        [Test]
        public void TestRoulette2()
        {
            var l = BufUtils.Random( 10000 ).AsEnumerable();
            l = l.Concat( BufUtils.Populate<byte>( 42, 10000 ) );
            var r = new I2PCore.Utils.RouletteSelection<byte, byte>( l, v => v, k => 1f );

            int is42 = 0;
            int samples = 10000;
            for ( int i = 0; i < samples; ++i )
            {
                if ( r.GetWeightedRandom( null ) == 42 ) ++is42;
            }

            Assert.IsTrue( is42 > samples / 1000 );
        }

        [Test]
        public void TestRoulette3()
        {
            var populationcount = RouletteSelection<float,float>.IncludeTop;
            var samplecount = 50000;

            var l = BufUtils.Populate<float>( () => BufUtils.RandomInt( 2 ) == 0 ? 0f : BufUtils.RandomFloat( 100000 ), populationcount );
            var r = new I2PCore.Utils.RouletteSelection<float, float>( l, v => v, k => k );

            int iszero = 0;
            for ( int i = 0; i < samplecount; ++i )
            {
                if ( r.GetWeightedRandom( null ) < float.Epsilon ) ++iszero;
            }

            Assert.IsTrue( iszero > 0 );
            Assert.IsTrue( iszero < samplecount / 2 );
        }

        [Test]
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
