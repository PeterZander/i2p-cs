using I2PCore.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace I2PTests
{
    
    
    /// <summary>
    ///This is a test class for StoreTest and is intended
    ///to contain all StoreTest Unit Tests
    ///</summary>
    [TestClass()]
    public class StoreTest
    {


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
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion

        /// <summary>
        ///A test for Write
        ///</summary>
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        [TestMethod()]
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
        ///A test for Store Constructor
        ///</summary>
        [TestMethod()]
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
        [TestMethod()]
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
