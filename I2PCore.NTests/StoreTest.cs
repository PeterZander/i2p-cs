using I2PCore.Utils;
using NUnit.Framework;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;

namespace I2PTests
{
    [TestFixture]
    public class StoreTest
    {
        [Test]
        public void WriteTest1()
        {
            var data1 = new byte[10000];
            data1.Randomize();

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[20000];
            data3.Randomize();

            var bl1 = new List<BufLen>();
            var bl2 = new List<BufLen>();
            var bl3 = new List<BufLen>();

            bl1.Add( new BufLen( data1 ) );
            bl1.Add( new BufLen( data2 ) );
            bl1.Add( new BufLen( data3 ) );

            bl2.Add( new BufLen( data2 ) );
            bl2.Add( new BufLen( data1 ) );
            bl2.Add( new BufLen( data3 ) );

            bl3.Add( new BufLen( data3 ) );
            bl3.Add( new BufLen( data1 ) );

            var filename = Path.GetFullPath( Path.GetTempFileName() );

            int defaultsectorsize = 100;
            while ( defaultsectorsize < 32000 )
            {
                using ( Store target = new Store( filename, defaultsectorsize ) )
                {
                    var ix = target.Write( bl1 );
                    Assert.AreEqual( bl1.Sum( bl => bl.Length ), target.GetDataLength( ix ) );
                    var copy = target.Read( ix );
                    var cbl = new BufRefLen( copy );
                    var cdata1 = cbl.ReadBufLen( data1.Length );
                    var cdata2 = cbl.ReadBufLen( data2.Length );
                    var cdata3 = cbl.ReadBufLen( data3.Length );
                    Assert.IsTrue( cdata1.Equals( data1 ) );
                    Assert.IsTrue( cdata2.Equals( data2 ) );
                    Assert.IsTrue( cdata3.Equals( data3 ) );

                    target.Write( bl2, ix );
                    Assert.AreEqual( bl2.Sum( bl => bl.Length ), target.GetDataLength( ix ) );
                    copy = target.Read( ix );
                    cbl = new BufRefLen( copy );
                    cdata2 = cbl.ReadBufLen( data2.Length );
                    cdata1 = cbl.ReadBufLen( data1.Length );
                    cdata3 = cbl.ReadBufLen( data3.Length );
                    Assert.IsTrue( cdata1.Equals( data1 ) );
                    Assert.IsTrue( cdata2.Equals( data2 ) );
                    Assert.IsTrue( cdata3.Equals( data3 ) );

                    target.Write( bl3, ix );
                    Assert.AreEqual( bl3.Sum( bl => bl.Length ), target.GetDataLength( ix ) );
                    copy = target.Read( ix );
                    cbl = new BufRefLen( copy );
                    cdata3 = cbl.ReadBufLen( data3.Length );
                    cdata1 = cbl.ReadBufLen( data1.Length );
                    Assert.IsTrue( cdata1.Equals( data1 ) );
                    Assert.IsTrue( cdata3.Equals( data3 ) );

                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
                File.Delete( filename );
            }
        }

        /// <summary>
        ///A test for Write
        ///</summary>
        [Test]
        public void WriteTest()
        {
            var data1 = new byte[10000];
            data1.Randomize();

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[200000];
            data3.Randomize();

            int defaultsectorsize = 30;
            while ( defaultsectorsize < 32000 )
            {
                using ( Stream dest = new MemoryStream() )
                {
                    using ( Store target = new Store( dest, defaultsectorsize ) )
                    {
                        var ix = target.Write( data1 );
                        Assert.AreEqual( data1.Length, target.GetDataLength( ix ) );
                        var copy = target.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                        target.Write( data2, ix );
                        Assert.AreEqual( data2.Length, target.GetDataLength( ix ) );
                        copy = target.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                        target.Write( data3, ix );
                        Assert.AreEqual( data3.Length, target.GetDataLength( ix ) );
                        copy = target.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                    }
                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
            }
        }

        /// <summary>
        ///A test for Save
        ///</summary>
        [Test]
        public void SaveTest()
        {
            int defaultsectorsize = 1024;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    for ( int i = 0; i < 1000; ++i ) target.Write( new byte[] { (byte)BufUtils.RandomInt( 240 ), (byte)( i % 5 ), (byte)BufUtils.RandomInt( 240 ) } );

                    var actual = target.GetMatching( r => r[1] == 3, 3 );
                    Assert.IsTrue( actual.Count == 200 );
                    Assert.IsTrue( actual.All( r => r.Value[1] == 3 ) );
                }

                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    var actual = target.GetMatching( r => true, 0 );
                    Assert.IsTrue( actual.Count == 1000 );

                    actual = target.GetMatching( r => r[1] == 3, 3 );
                    Assert.IsTrue( actual.Count == 200 );
                    Assert.IsTrue( actual.All( r => r.Value[1] == 3 ) );
                }
            }
        }

        /// <summary>
        ///A test for ReadAll
        ///</summary>
        [Test]
        public void ReadAllTest()
        {
            int defaultsectorsize = 300;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    for ( int i = 0; i < 1000; ++i ) target.Write( new byte[] { (byte)BufUtils.RandomInt( 240 ), (byte)( i % 5 ), (byte)BufUtils.RandomInt( 240 ) } );

                    var allofit = target.ReadAll();

                    Assert.IsTrue( allofit.Count == 1000 );
                    for ( int i = 0; i < 1000; ++i ) Assert.IsTrue( allofit[i].Value[1] == ( i % 5 ) );
                }
            }
        }

        /// <summary>
        ///A test for Next
        ///</summary>
        [Test]
        public void NextTest()
        {
            int defaultsectorsize = 60;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    for ( int i = 0; i < 1000; ++i ) target.Write( new byte[] { (byte)BufUtils.RandomInt( 240 ), (byte)( i % 7 ), (byte)BufUtils.RandomInt( 240 ) } );

                    var ix = 0;
                    var cnt = 0;
                    while ( ( ix = target.Next( ix ) ) != -1 )
                    {
                        var item = target.Read( ix );
                        Assert.IsTrue( item[1] == ( cnt % 7 ) );
                        ++cnt;
                    }

                    Assert.IsTrue( cnt == 1000 );
                }
            }
        }

        /// <summary>
        ///A test for IndexStream
        ///</summary>
        [Test]
        public void IndexStreamTest()
        {
            var data1 = new byte[100000];
            data1.Randomize();
            //for ( int i = 0; i < data1.Length; ++i ) data1[i] = (byte)( i & 0xff );

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[200000];
            data3.Randomize();

            int defaultsectorsize = 17;
            while ( defaultsectorsize < 32000 )
            {
                using ( Stream dest = new MemoryStream() )
                {
                    using ( Store target = new Store( dest, defaultsectorsize ) )
                    {
                        var ixstream = target.IndexStream();
                        using ( var innerstore = new Store( ixstream, defaultsectorsize * 2 + BufUtils.RandomInt( 12 ) ) )
                        {
                            var ix = innerstore.Write( data1 );
                            Assert.AreEqual( data1.Length, innerstore.GetDataLength( ix ) );
                            var copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                            innerstore.Write( data2, ix );
                            Assert.AreEqual( data2.Length, innerstore.GetDataLength( ix ) );
                            copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                            innerstore.Write( data3, ix );
                            Assert.AreEqual( data3.Length, innerstore.GetDataLength( ix ) );
                            copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                        }
                    }
                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
            }
        }

        /// <summary>
        ///A test for IndexStream
        ///</summary>
        [Test]
        public void IndexStreamTest1()
        {
            var data1 = new byte[10000];
            data1.Randomize();
            //for ( int i = 0; i < data1.Length; ++i ) data1[i] = (byte)( i & 0xff );

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[20000];
            data3.Randomize();

            var filename = Path.GetFullPath( Path.GetTempFileName() );

            int defaultsectorsize = 17;
            while ( defaultsectorsize < 32000 )
            {
                using ( Store target = new Store( filename, defaultsectorsize ) )
                {
                    var ixstream = target.IndexStream();
                    using ( var innerstore = new Store( ixstream, defaultsectorsize * 2 + BufUtils.RandomInt( 12 ) ) )
                    {
                        var ix = innerstore.Write( data1 );
                        Assert.AreEqual( data1.Length, innerstore.GetDataLength( ix ) );
                        var copy = innerstore.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                        innerstore.Write( data2, ix );
                        Assert.AreEqual( data2.Length, innerstore.GetDataLength( ix ) );
                        copy = innerstore.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                        innerstore.Write( data3, ix );
                        Assert.AreEqual( data3.Length, innerstore.GetDataLength( ix ) );
                        copy = innerstore.Read( ix );
                        Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                    }
                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
                File.Delete( filename );
            }
        }

        /// <summary>
        ///A test for IndexStream with offset
        ///</summary>
        [Test]
        public void IndexStreamTest2()
        {
            var data1 = new byte[100000];
            data1.Randomize();
            //for ( int i = 0; i < data1.Length; ++i ) data1[i] = (byte)( i & 0xff );

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[200000];
            data3.Randomize();

            int defaultsectorsize = 17;
            while ( defaultsectorsize < 32000 )
            {
                using ( Stream dest = new MemoryStream() )
                {
                    using ( Store target = new Store( dest, defaultsectorsize ) )
                    {
                        var firstsector = new byte[target.FirstSectorDataSize];
                        firstsector.Populate<byte>( 1 );

                        var threesectors = new byte[target.SectorDataSize * 3];
                        threesectors.Populate<byte>( 2 );

                        var towrite = new BufLen[] { new BufLen( firstsector ), new BufLen( threesectors ) };

                        var strix = target.Write( towrite );

                        var ixstream = BufUtils.RandomInt( 10 ) > 5 ? 
                            target.IndexStream( strix, true ) :
                            target.IndexStream( strix, target.FirstSectorDataSize + BufUtils.RandomInt( 20 ) );
                        using ( var innerstore = new Store( ixstream, defaultsectorsize * 2 + BufUtils.RandomInt( 12 ) ) )
                        {
                            var ix = innerstore.Write( data1 );
                            Assert.AreEqual( data1.Length, innerstore.GetDataLength( ix ) );
                            var copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                            innerstore.Write( data2, ix );
                            Assert.AreEqual( data2.Length, innerstore.GetDataLength( ix ) );
                            copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                            innerstore.Write( data3, ix );
                            Assert.AreEqual( data3.Length, innerstore.GetDataLength( ix ) );
                            copy = innerstore.Read( ix );
                            Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                        }

                        Assert.IsTrue( BufUtils.Equal( target.Read( strix ).Take( firstsector.Length ).ToArray(), firstsector ) );
                    }
                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
            }
        }

        /// <summary>
        /// Stress IndexStream
        ///</summary>
        [Test]
        public void IndexStreamTest3()
        {
            const int MaxSectorSize = 32000;

            var data1 = new byte[100000];
            data1.Randomize();
            //for ( int i = 0; i < data1.Length; ++i ) data1[i] = (byte)( i & 0xff );

            var data2 = new byte[1000];
            data2.Randomize();

            var data3 = new byte[200000];
            data3.Randomize();

            int defaultsectorsize = 17;
            while ( defaultsectorsize < MaxSectorSize )
            {
                using ( Stream dest = new MemoryStream() )
                {
                    using ( Store mid = new Store( dest, MaxSectorSize - defaultsectorsize + BufUtils.RandomInt( 15 ) ) )
                    {
                        mid.IndexStream();
                        mid.IndexStream();
                        mid.IndexStream();

                        using ( Store target = new Store( mid.IndexStream(), defaultsectorsize ) )
                        {
                            var firstsector = new byte[target.FirstSectorDataSize];
                            firstsector.Populate<byte>( 1 );

                            var threesectors = new byte[target.SectorDataSize * 3];
                            threesectors.Populate<byte>( 2 );

                            var towrite = new BufLen[] { new BufLen( firstsector ), new BufLen( threesectors ) };

                            var strix = target.Write( towrite );

                            var ixstream = BufUtils.RandomInt( 10 ) > 5 ?
                                target.IndexStream( strix, true ) :
                                target.IndexStream( strix, target.FirstSectorDataSize + BufUtils.RandomInt( 20 ) );
                            using ( var innerstore = new Store( ixstream, defaultsectorsize * 2 + BufUtils.RandomInt( 12 ) ) )
                            {
                                var ix = innerstore.Write( data1 );
                                Assert.AreEqual( data1.Length, innerstore.GetDataLength( ix ) );
                                var copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                                innerstore.Write( data2, ix );
                                Assert.AreEqual( data2.Length, innerstore.GetDataLength( ix ) );
                                copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                                innerstore.Write( data3, ix );
                                Assert.AreEqual( data3.Length, innerstore.GetDataLength( ix ) );
                                copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                            }

                            using ( var innerstore = new Store( target.IndexStream(), defaultsectorsize * 2 + BufUtils.RandomInt( 12 ) ) )
                            {
                                var ix = innerstore.Write( data1 );
                                Assert.AreEqual( data1.Length, innerstore.GetDataLength( ix ) );
                                var copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data1 ) );

                                innerstore.Write( data2, ix );
                                Assert.AreEqual( data2.Length, innerstore.GetDataLength( ix ) );
                                copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data2 ) );

                                innerstore.Write( data3, ix );
                                Assert.AreEqual( data3.Length, innerstore.GetDataLength( ix ) );
                                copy = innerstore.Read( ix );
                                Assert.IsTrue( BufUtils.Equal( copy, data3 ) );
                            }

                            Assert.IsTrue( BufUtils.Equal( target.Read( strix ).Take( firstsector.Length ).ToArray(), firstsector ) );
                        }
                    }
                }

                defaultsectorsize = (int)( defaultsectorsize * 1.8 );
            }
        }

        /// <summary>
        ///A test for GetMatchingIx
        ///</summary>
        [Test]
        public void GetMatchingIxTest()
        {
            int defaultsectorsize = 1024;
            int multiplier = 200;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    for ( int i = 0; i < 10 * multiplier; ++i ) target.Write( new byte[] { (byte)BufUtils.RandomInt( 240 ), (byte)( i % 5 ), (byte)BufUtils.RandomInt( 240 ) } );

                    var actual = target.GetMatchingIx( r => r[1] == 3, 2 );
                    Assert.IsTrue( actual.Count == 2 * multiplier );

                    actual = target.GetMatchingIx( r => r.Length > 1 ? r[1] == 3: false, 1 );
                    Assert.IsTrue( actual.Count == 0 );

                    actual = target.GetMatchingIx( r => r[1] == 3, 3 );
                    Assert.IsTrue( actual.Count == 2 * multiplier );
                }
            }
        }

        /// <summary>
        ///A test for GetMatching
        ///</summary>
        [Test]
        public void GetMatchingTest()
        {
            int defaultsectorsize = 1024;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    for ( int i = 0; i < 1000; ++i ) target.Write( new byte[] { (byte)BufUtils.RandomInt( 240 ), (byte)( i % 5 ), (byte)BufUtils.RandomInt( 240 ) } );

                    var actual = target.GetMatching( r => r[1] == 3, 2 );
                    Assert.IsTrue( actual.Count == 200 );
                    Assert.IsTrue( actual.All( r => r.Value[1] == 3 ) );

                    actual = target.GetMatching( r => r.Length > 1 ? r[1] == 3 : false, 1 );
                    Assert.IsTrue( actual.Count == 0 );
                    Assert.IsTrue( actual.All( r => r.Value[1] == 3 ) );

                    actual = target.GetMatching( r => r[1] == 3, 3 );
                    Assert.IsTrue( actual.Count == 200 );
                    Assert.IsTrue( actual.All( r => r.Value[1] == 3 ) );
                }
            }
        }

        /// <summary>
        ///A test for GetDataLength
        ///</summary>
        [Test]
        public void GetDataLengthTest()
        {
            int defaultsectorsize = 1024;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    var data = new byte[512];
                    data.Randomize();

                    var data1 = new byte[720];
                    data1.Randomize();

                    var ix = target.Write( data );
                    Assert.IsTrue( target.GetDataLength( ix ) == data.Length );

                    target.Write( data ); 
                    var ix2 = target.Write( data );
                    target.Write( data );
                    ix = target.Write( data1 );
                    target.Write( data );
                    target.Write( data );

                    Assert.IsTrue( target.GetDataLength( ix ) == data1.Length );
                    Assert.IsTrue( target.GetDataLength( ix2 ) == data.Length );
                }
            }
        }

        /// <summary>
        ///A test for Delete
        ///</summary>
        [Test]
        public void DeleteTest1()
        {
            int defaultsectorsize = 128;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    var data = new byte[512];
                    data.Randomize();

                    var data1 = new byte[720];
                    data1.Randomize();

                    for ( int i = 0; i < 1000; ++i ) target.Write( ( i & 1 ) != 0 ? data1 : data );

                    var fullsize = dest.Length;

                    var todelete = target.GetMatchingIx( r => r.Length == 720, 1024 );

                    target.Delete( todelete );
                    Assert.IsTrue( dest.Length <= fullsize );
                    var checkset = target.GetMatchingIx( r => r.Length == 720, 1024 );
                    Assert.IsTrue( checkset.Count == 0 );

                    try
                    {
                        foreach ( var oneix in todelete ) target.Write( data, oneix );
                        Assert.Fail( "Updating a deleted sector should fail" );
                    }
                    catch ( System.Exception )
                    {
                    }

                    foreach ( var oneix in todelete ) target.Write( data );
                    Assert.IsTrue( dest.Length <= fullsize ); // data is smaller than data1

                    var togrow = target.GetMatchingIx( r => r.Length == 512, 1024 );
                    foreach ( var oneix in togrow ) target.Write( data1, oneix );
                    Assert.IsTrue( dest.Length > fullsize );
                }
            }
        }

        /// <summary>
        ///A test for Delete
        ///</summary>
        [Test]
        public void DeleteTest()
        {
            int defaultsectorsize = 128;
            using ( Stream dest = new MemoryStream() )
            {
                using ( Store target = new Store( dest, defaultsectorsize ) )
                {
                    var data = new byte[512];
                    data.Randomize();

                    var data1 = new byte[720];
                    data1.Randomize();

                    for ( int i = 0; i < 1000; ++i ) target.Write( ( i & 1 ) != 0 ? data1 : data );

                    var fullsize = dest.Length;

                    var todelete = target.GetMatchingIx( r => r.Length == 720, 1024 );

                    foreach ( var oneix in todelete ) target.Delete( oneix );
                    Assert.IsTrue( dest.Length <= fullsize );
                    var checkset = target.GetMatchingIx( r => r.Length == 720, 1024 );
                    Assert.IsTrue( checkset.Count == 0 );

                    try
                    {
                        foreach ( var oneix in todelete ) target.Write( data, oneix );
                        Assert.Fail( "Updating a deleted sector should fail" );
                    }
                    catch ( System.Exception )
                    {
                    }

                    foreach ( var oneix in todelete ) target.Write( data );
                    Assert.IsTrue( dest.Length <= fullsize ); // data is smaller than data1

                    var togrow = target.GetMatchingIx( r => r.Length == 512, 1024 );
                    foreach ( var oneix in togrow ) target.Write( data1, oneix );
                    Assert.IsTrue( dest.Length > fullsize );
                }
            }
        }

        /// <summary>
        /// A test of BitmapToPos
        ///</summary>
        [Test]
        public void BitmapToPosTest()
        {
            var sectorsize = 768;
            var reservedsectors = 1;

            int v = 3;

            Assert.IsTrue( BitmapSector.BitmapToPos( v, sectorsize ) == 
                ( reservedsectors + v ) * sectorsize );
        }

        /// <summary>
        /// A test of PosToBitmap
        ///</summary>
        [Test]
        public void PosToBitmapTest()
        {
            var sectorsize = 1845;
            var reservedsectors = 1;

            int v = 7;
            var bix = BitmapSector.PosToBitmap( v, sectorsize );
            Assert.IsTrue( bix == -reservedsectors + 0 );

            v = 8423;
            bix = BitmapSector.PosToBitmap( v, sectorsize );
            Assert.IsTrue( bix ==
                -reservedsectors + (int)Math.Floor( (double)v / sectorsize ) );
        }

        /// <summary>
        /// Testing file format tag verification
        /// </summary>
        [Test]
        public void StoreFileFormatVersionTest()
        {
            try
            {
                var data = new byte[] { 0x23, 0x5c, 0xff, 0x00, 0x27 };
                Stream dest = new MemoryStream();
                int defaultsectorsize = 128;

                StoreFileFormatVersionTest_OneRun( data, dest, defaultsectorsize );

                dest.Position = 0;
                var destmem = StreamUtils.Read( dest );

                var news = new MemoryStream();
                news.Write( destmem );
                news.Position = 0;

                StoreFileFormatVersionTest_OneRun( data, news, defaultsectorsize );

                destmem[6] = 0;
                news = new MemoryStream();
                news.Write( destmem );
                news.Position = 0;

                try
                {
                    StoreFileFormatVersionTest_OneRun( data, news, defaultsectorsize );
                    Assert.Fail( "Should throw execptions" );
                }
                catch ( IOException )
                {
                    Assert.IsTrue( true );
                }
            }
            catch ( IOException )
            {
                Assert.Fail( "Should not throw execptions" );
            }
        }

        private static void StoreFileFormatVersionTest_OneRun( byte[] data, Stream dest, int defaultsectorsize )
        {
            using ( Store target = new Store( dest, defaultsectorsize ) )
            {
                var ix = target.Write( data );
                Assert.AreEqual( data.Length, target.GetDataLength( ix ) );
                var copy = target.Read( ix );
                Assert.IsTrue( BufUtils.Equal( copy, data ) );
            }
        }

        /// <summary>
        /// A test of record alignment
        /// All records should be alligned to sector size chunks to enable
        /// efficient storage on block devices with fixed sector size.
        ///</summary>
        [Test]
        public void StoreRecordAlignment()
        {
            var data = new byte[] { 0x5c, 0xff, 0x00, 0x27 };
            Stream dest = new MemoryStream();
            int defaultsectorsize = 4096;
            using ( Store target = new Store( dest, defaultsectorsize ) )
            {
                var ix = target.Write( data );
                Assert.AreEqual( data.Length, target.GetDataLength( ix ) );
                var copy = target.Read( ix );
                Assert.IsTrue( BufUtils.Equal( copy, data ) );

                var sectorstart = target.BitmapToPos( ix );
                Assert.IsTrue( sectorstart % defaultsectorsize == 0 );
            }
        }

        /// <summary>
        ///A test for Store Constructor
        ///</summary>
        [Test]
        public void StoreConstructorTest1()
        {
            var data = new byte[] { 0x5c, 0xff, 0x00, 0x27 };
            var filename = Path.GetFullPath( Path.GetTempFileName() );
            int defaultsectorsize = 1024;
            using ( Store target = new Store( filename, defaultsectorsize ) )
            {
                var ix = target.Write( data );
                Assert.AreEqual( data.Length, target.GetDataLength( ix ) );
                var copy = target.Read( ix );
                Assert.IsTrue( BufUtils.Equal( copy, data ) );
            }
            File.Delete( filename );
        }

        /// <summary>
        ///A test for Store Constructor
        ///</summary>
        [Test]
        public void StoreConstructorTest()
        {
            var data = new byte[] { 0x5c, 0xff, 0x00, 0x27 };
            Stream dest = new MemoryStream();
            int defaultsectorsize = 1024;
            using ( Store target = new Store( dest, defaultsectorsize ) )
            {
                var ix = target.Write( data );
                Assert.AreEqual( data.Length, target.GetDataLength( ix ) );
                var copy = target.Read( ix );
                Assert.IsTrue( BufUtils.Equal( copy, data ) );
            }
        }
    }
}
