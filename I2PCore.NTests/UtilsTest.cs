using System;
using System.Text;
using System.Linq;
using NUnit.Framework;
using I2PCore.Utils;
using System.Threading;

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
            var smalldata = BufUtils.RandomBytes( 200 );
            var bigdata = BufUtils.RandomBytes( 2 * 1024 * 1024 );

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
            int samples = 10000;
            var ftweight = 30f;
            var minexpected = samples / ( 256 * 2 );

            for ( int runs = 0; runs < 20; ++runs )
            {
                var l = BufUtils.RandomBytes( samples ).AsEnumerable();
                var r = new I2PCore.Utils.RouletteSelection<byte, byte>( l, v => v, k => k == 42 ? ftweight : 1f, samples );

                int is42 = l.Sum( _ => r.GetWeightedRandom( null ) == 42 ? 1 : 0 );

                Assert.IsTrue( is42 > minexpected );
            }
        }

        [Test]
        public void TestRoulette2()
        {
            int samples = 10000;
            var l = BufUtils.RandomBytes( samples ).AsEnumerable();
            l = l.Concat( BufUtils.Populate<byte>( 42, 10000 ) );
            var r = new I2PCore.Utils.RouletteSelection<byte, byte>( l, v => v, k => 1f, samples );

            int is42 = 0;

            for ( int i = 0; i < samples; ++i )
            {
                if ( r.GetWeightedRandom( null ) == 42 ) ++is42;
            }

            Assert.IsTrue( is42 > samples / 1000 );
        }

        [Test]
        public void TestRoulette3()
        {
            var samplecount = 50000;
            var populationcount = samplecount / 2;

            var l = BufUtils.Populate<float>( () => BufUtils.RandomInt( 2 ) == 0 ? 0f : BufUtils.RandomFloat( 100000 ), populationcount );
            var r = new I2PCore.Utils.RouletteSelection<float, float>( l, v => v, k => k, populationcount );

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

        [Test]
        public void TestRandomDouble()
        {
            var values = Enumerable
                    .Range( 0, 10000 )
                    .Select( i => BufUtils.RandomDouble() )
                    .ToArray();

            var expecteddev = Math.Sqrt( 1.0 / 12 );

            Assert.IsTrue( values.All( b => b >= 0.0 && b < 1.0 ) );
            Assert.IsTrue( Math.Abs( values.Average() - 0.5 ) < 0.02 );

            var sddiff = values.Select( v => (float) v).StdDev() - expecteddev;
            Assert.IsTrue( Math.Abs( sddiff ) < 0.01 );
        }

        [Test]
        public void TestRandom()
        {
            var values = Enumerable
                    .Range( 0, 21 )
                    .Select( i => BufUtils.RandomDouble( 1.0 ) )
                    .ToArray();

            var bag = values.SelectMany( i1 => values.SelectMany( i2 => values ) )
                    .Select( i3 => values.Random() );

            Assert.IsTrue( bag.All( b => values.Any( i => i == b ) ) );
        }
        [Test]
        public void TestShuffle()
        {
            var ints = Enumerable.Range( 0, 200 );
            var bag = ints.Select( i => BufUtils.RandomDouble( 1 ) ).ToArray();
            var shuffled = bag.Shuffle().ToArray();
            Assert.IsTrue( bag.All( b => shuffled.Any( i => i == b ) ) );
        }

        class TWI
        {
            public bool IsDisposed { get; protected set; } = false;
        }

        class TWId : TWI, IDisposable
        {
            void IDisposable.Dispose()
            {
                IsDisposed = true;
            }
        }

        [Test]
        public void TestTimeWindowDictionary()
        {
            var twd = new TimeWindowDictionary<int, TWI>( TickSpan.Seconds( 1 ) );

            var oneinstance = new TWI();

            twd[1] = oneinstance;
            twd[100] = oneinstance;
            twd[101] = oneinstance;
            Assert.IsFalse( twd[1].IsDisposed );

            System.Threading.Thread.Sleep( 1100 );

            Assert.IsFalse( oneinstance.IsDisposed );
            Assert.IsFalse( twd.TryGetValue( 1, out _ ) );

            oneinstance = new TWId();

            twd[2] = oneinstance;
            Assert.IsFalse( twd[2].IsDisposed );

            System.Threading.Thread.Sleep( 1100 );

            Assert.IsFalse( twd.TryGetValue( 2, out _ ) );

            ( (IDisposable)twd ).Dispose();
            Assert.IsTrue( oneinstance.IsDisposed );
        }

        [Test]
        public void TestNetworkMaskIPV4Construction()
        {
            var nm1 = new IPAddressMask( "0.0.0.0//0.0.0.255" );
            Assert.IsTrue( nm1.Address.GetAddressBytes().All( b => b == 0 ) );
            Assert.IsTrue( nm1.Mask.GetAddressBytes()[3] == 0xff );

            var nm2 = new IPAddressMask( "0.0.0.255/8" );
            var nm2ab = nm2.Address.GetAddressBytes();
            var nm2nm = nm2.Mask.GetAddressBytes();
            Assert.IsTrue( nm2ab.Select( b => (int)b ).Sum() == 0xff );
            Assert.IsTrue( nm2nm[0] == 0xff );
            Assert.IsTrue( nm2nm[1] == 0x00 );

            var nm3 = new IPAddressMask( "92.00.00.255/19" );
            var nm3ab = nm3.Address.GetAddressBytes();
            var nm3nm = nm3.Mask.GetAddressBytes();
            Assert.IsTrue( nm3ab[0] == 0x5c );
            Assert.IsTrue( nm3ab[3] == 0xff );
            Assert.IsTrue( nm3nm[0] == 0xff );
            Assert.IsTrue( nm3nm[1] == 0xff );
            Assert.IsTrue( nm3nm[2] == 0xe0 );

            var nm4 = new IPAddressMask( "00.92.255.00/32" );
            var nm4ab = nm4.Address.GetAddressBytes();
            var nm4nm = nm4.Mask.GetAddressBytes();
            Assert.IsTrue( nm4ab[1] == 0x5c );
            Assert.IsTrue( nm4ab[2] == 0xff );
            Assert.IsTrue( nm4nm.All( b => b == 0xff ) );
        }

        [Test]
        public void TestNetworkMaskIPV6Construction()
        {
            var nm1 = new IPAddressMask( "::0//::ff" );
            Assert.IsTrue( nm1.Address.GetAddressBytes().All( b => b == 0 ) );
            Assert.IsTrue( nm1.Mask.GetAddressBytes()[15] == 0xff );

            var nm2 = new IPAddressMask( "::ff/16" );
            var nm2ab = nm2.Address.GetAddressBytes();
            var nm2nm = nm2.Mask.GetAddressBytes();
            Assert.IsTrue( nm2ab.Select( b => (int)b ).Sum() == 0xff );
            Assert.IsTrue( nm2nm[0] == 0xff );
            Assert.IsTrue( nm2nm[1] == 0xff );
            Assert.IsTrue( nm2nm[2] == 0x00 );

            var nm3 = new IPAddressMask( "5c00::00ff/19" );
            var nm3ab = nm3.Address.GetAddressBytes();
            var nm3nm = nm3.Mask.GetAddressBytes();
            Assert.IsTrue( nm3ab[0] == 0x5c );
            Assert.IsTrue( nm3ab[15] == 0xff );
            Assert.IsTrue( nm3nm[0] == 0xff );
            Assert.IsTrue( nm3nm[1] == 0xff );
            Assert.IsTrue( nm3nm[2] == 0xe0 );

            var nm4 = new IPAddressMask( "005c::ff00/128" );
            var nm4ab = nm4.Address.GetAddressBytes();
            var nm4nm = nm4.Mask.GetAddressBytes();
            Assert.IsTrue( nm4ab[1] == 0x5c );
            Assert.IsTrue( nm4ab[14] == 0xff );
            Assert.IsTrue( nm4nm.All( b => b == 0xff ) );
        }

        [Test]
        public void TestNetworkMaskIPV4BelongsTo()
        {
            var nm1 = new IPAddressMask( "192.168.255.255/16" );
            var a1belongs = System.Net.IPAddress.Parse( "192.168.2.52" );
            var a1not = System.Net.IPAddress.Parse( "192.169.2.52" );

            Assert.IsTrue( nm1.BelongsTo( a1belongs ) );
            Assert.IsFalse( nm1.BelongsTo( a1not ) );
        }

        [Test]
        public void TestNetworkMaskIPV6BelongsTo()
        {
            var nm1 = new IPAddressMask( "fe00::ffff/9" );
            var a1belongs = System.Net.IPAddress.Parse( "fe00::12:13:14" );
            var a1not = System.Net.IPAddress.Parse( "2001::12:13:14" );

            Assert.IsTrue( nm1.BelongsTo( a1belongs ) );
            Assert.IsFalse( nm1.BelongsTo( a1not ) );
        }

        [Test]
        public void TestRunBatchWait()
        {
            const int TestCount = 1000;
            var rbw = new RunBatchWait( TestCount );

            for( int i = 0; i < TestCount; ++i )
            {
                if ( i % 15 == 0 ) Thread.Sleep( 50 );

                if ( !ThreadPool.QueueUserWorkItem( cb => 
                {
                    Thread.Sleep( 10 );
                    rbw.Set();
                } ) )
                {
                    rbw.Set();
                }
            }

            if ( !rbw.WaitOne( 2000 ) )
            {
                Assert.Fail();
            }

        }

        [Test]
        public void TestMurMurHash3()
        {
            // From https://stackoverflow.com/questions/14747343/murmurhash3-test-vectors
            /*
                | Input        | Seed       | Expected   |
                |--------------|------------|------------|
                | (no bytes)   | 0          | 0          | with zero data and zero seed, everything becomes zero
                | (no bytes)   | 1          | 0x514E28B7 | ignores nearly all the math
                | (no bytes)   | 0xffffffff | 0x81F16F39 | make sure your seed uses unsigned 32-bit math
                | FF FF FF FF  | 0          | 0x76293B50 | make sure 4-byte chunks use unsigned math
                | 21 43 65 87  | 0          | 0xF55B516B | Endian order. UInt32 should end up as 0x87654321
                | 21 43 65 87  | 0x5082EDEE | 0x2362F9DE | Special seed value eliminates initial key with xor
                | 21 43 65     | 0          | 0x7E4A8634 | Only three bytes. Should end up as 0x654321
                | 21 43        | 0          | 0xA0F7B07A | Only two bytes. Should end up as 0x4321
                | 21           | 0          | 0x72661CF4 | Only one byte. Should end up as 0x21
                | 00 00 00 00  | 0          | 0x2362F9DE | Make sure compiler doesn't see zero and convert to null
                | 00 00 00     | 0          | 0x85F0B427 | 
                | 00 00        | 0          | 0x30F4C306 |
                | 00           | 0          | 0x514E28B7 |            
            */

            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[0] ), 0 ) == 0 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[0] ), 1 ) == 0x514E28B7 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[0] ), 0xffffffff ) == 0x81F16F39 );

            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0xff, 0xff, 0xff, 0xff } ), 0 ) == 0x76293B50 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0x21, 0x43, 0x65, 0x87 } ), 0 ) == 0xF55B516B );

            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0x21, 0x43, 0x65, 0x87 } ), 0x5082EDEE ) == 0x2362F9DE );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0x21, 0x43, 0x65 } ), 0 ) == 0x7E4A8634 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0x21, 0x43 } ), 0 ) == 0xA0F7B07A );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0x21 } ), 0 ) == 0x72661CF4 );

            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0, 0, 0, 0 } ), 0 ) == 0x2362F9DE );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0, 0, 0 } ), 0 ) == 0x85F0B427 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0, 0 } ), 0 ) == 0x30F4C306 );
            Assert.IsTrue( MurMurHash3.Hash( new BufRefLen( new byte[] { 0 } ), 0 ) == 0x514E28B7 );
        }
    }
}