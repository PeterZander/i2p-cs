#define NO_STORE_DETAILED_TRACE_LOGS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections;

// [Sektor-chunk storlek: 4 bytes][ 1st sector reserved ]
//     [Sector typ: 1 byte 0x01 = bitmap]
//     [Sector typ: 1 byte 0x02 = Datasector]
//     [Sector typ: 1 byte 0x04 = Fortsättning]
//     [Sector typ: 1 byte 0x08 = Key / Metadata]
//     [Sector typ: 1 byte 0xFF = Allocated / Not initialized]
//         [Nästa sektor index 4 bytes, eller $FFFFFFFF om sista bitmap sector.]
//             [Bitmap. 1 bit per sektor: 1 Allokerad. 0. Fri. Sektor-chunk storlek - 5 bytes]
//             [Icke Fortsättning]
//                  [Nästa sector ix: 4 bytes (* sectorsize för position) eller $FFFFFFFF][Total Datalängd bytes 8 bytes][Sektor "Sektor-chunk storlek" - 13 bytes]
//             [Fortsättning]
//                  [Nästa sector ix: 4 bytes (* sectorsize för position)][Sektor "Sektor-chunk storlek" - 5 bytes]
//     
// Identitet: Filposition. Första positionen för Icke-fortsättningsector. Remove (x)   
// bit -> sector: 16 + bit * Sektor-chunk
// Sector pos -> bit : ( Sector-pos - 16 ) / Sector chunk.

namespace I2PCore.Utils
{
    class BitmapSector
    {
        Store TheStore;

        byte[] SectorData;

        int SectorSize;

        private int StoreIx = -1; // -1 Not initialized

        internal int Ix
        {
            get { return StoreIx; }
            set { StoreIx = value; }
        }

        BitmapSector NextBitmap = null;

        public BitmapSector( Store thestore, int ix, int sectorsize )
        {
            if ( ix < 0 ) throw new ArgumentException( "Starting sector needed" );

            SectorSize = sectorsize;
            Ix = ix;
            TheStore = thestore;

            Load();
        }

        private void Load()
        {
            if ( TheStore.TheFile.Length >= BitmapToPos( Ix + 1 ) )
            {
                TheStore.TheFile.Position = BitmapToPos( Ix );

                if ( StreamUtils.ReadInt8( TheStore.TheFile ) != (byte)Store.SectorTypes.Bitmap )
                {
                    InitializeBitmapSector();
                    return;
                }

				var nextix = StreamUtils.ReadInt32( TheStore.TheFile );
				SectorData = StreamUtils.Read( TheStore.TheFile, (int)TheStore.SectorDataSize );

                if ( nextix != Store.LAST_SECTOR_IN_CHAIN )
                {
                    NextBitmap = new BitmapSector( TheStore, nextix, SectorSize );
                }
                else
                {
                    NextBitmap = null;
                }
            }
            else
            {
                InitializeBitmapSector();
            }
        }

        private void InitializeBitmapSector()
        {
            TheStore.TheFile.Position = BitmapToPos( Ix );

            StreamUtils.WriteUInt8( TheStore.TheFile, (byte)Store.SectorTypes.Bitmap );
            StreamUtils.WriteInt32( TheStore.TheFile, Store.LAST_SECTOR_IN_CHAIN );
            NextBitmap = null;

            SectorData = new byte[(int)TheStore.SectorDataSize];
            StreamUtils.Write( TheStore.TheFile, SectorData );
        }

        internal int BitsPerSector { get { return SectorData.Length * 8; } }
        internal int TotalBitCount { get { return NextBitmap != null ? NextBitmap.TotalBitCount + BitsPerSector: BitsPerSector; } }

        public bool this[int ix]
        {
            get
            {
                if ( ix >= BitsPerSector )
                {
                    if ( NextBitmap == null )
                    {
                        // Never allocated
                        return false;
                    }

                    return NextBitmap[ix - BitsPerSector];
                }

                return ( ( SectorData[ix / 8] >> ( ix % 8 ) ) & 0x01 ) != 0;
            }
            set
            {
                if ( ix >= BitsPerSector )
                {
                    if ( NextBitmap == null )
                    {
                        if ( !value ) return; // Already not unallocated

                        throw new NullReferenceException( "Bitmap should always be pre allocated!" );
                    }
                    NextBitmap[ix - BitsPerSector] = value;
                    return;
                }

                var offset = ix / 8;
                var bit = ix % 8;
                var mask = 0x01 << bit;
                var result = (byte)( ( SectorData[offset] & ( ~mask ) ) | ( value ? mask : 0 ) );
                SectorData[offset] = result;

                TheStore.TheFile.Position = BitmapToPos( Ix ) + Store.SectorHeaderSize + offset;
                TheStore.TheFile.WriteByte( result );
            }
        }

        internal int ExtendBitmapSpace()
        {
            var ix = TotalBitCount;
            TheStore.TheFile.Position = BitmapToPos( ix );
            TheStore.TheFile.WriteByte( (byte)Store.SectorTypes.Unallocated );

            var next = new BitmapSector( TheStore, ix, SectorSize );

#if STORE_DETAILED_TRACE_LOGS
            System.Diagnostics.Debug.WriteLine( "ExtendBitmapSpace: " + ix.ToString() );
#endif

            // Its now reserving space for itself
            next[0] = true;

            var lastbitmapsector = TheStore.Bits;
            while ( lastbitmapsector.NextBitmap != null ) lastbitmapsector = lastbitmapsector.NextBitmap;
            lastbitmapsector.NextBitmap = next;
            TheStore.TheFile.Position = BitmapToPos( lastbitmapsector.Ix ) + 1;
            StreamUtils.WriteInt32( TheStore.TheFile, next.Ix );

            return ix;
        }

        internal bool FindFreeSector( out int ix, int offset )
        {
            for ( int i = 0; i < SectorData.Length; ++i )
            {
                var val = SectorData[i];
                if ( val != 0xff )
                {
                    var delta = 0;

                    while( true )
                    {
                        if ( ( val & 0x01 ) == 0 )
                        {
                            ix = offset + i * 8 + delta;
                            return true;
                        }
                        val >>= 1;
                        ++delta;
                    };
                }
            }
            if ( NextBitmap != null ) return NextBitmap.FindFreeSector( out ix, offset + BitsPerSector );

            ix = -1;
            return false;
        }

        internal List<BitmapSector> All()
        {
            var result = new List<BitmapSector>();

            var next = this;
            while ( next != null )
            {
                result.Add( next );
                next = next.NextBitmap;
            }

            return result;
        }

        long BitmapToPos( int bit )
        {
            return BitmapToPos( bit, SectorSize );
        }

        int PosToBitmap( long pos )
        {
            return PosToBitmap( pos, SectorSize );
        }

        public static long BitmapToPos( int bit, int sectorsize )
        {
            var result = ( bit + 1 ) * sectorsize;
#if STORE_DETAILED_TRACE_LOGS
            System.Diagnostics.Debug.WriteLine( string.Format( "BitmapToPos: Bit: {0}, sectorsize: {1}, result: {2}", bit, sectorsize, result ) );
#endif
            return result;
        }

        public static int PosToBitmap( long pos, int sectorsize )
        {
            var result = (int)( pos / sectorsize - 1 );
#if STORE_DETAILED_TRACE_LOGS
            System.Diagnostics.Debug.WriteLine( string.Format( "PosToBitmap: Pos: {0}, sectorsize: {1}, result: {2}", pos, sectorsize, result ) );
#endif
            return result;
        }
    }

    public class Store: IDisposable
    {
        /// <summary>
        /// "Allocated" is allocated (reserved in bitmap) but not initialized.
        /// </summary>
        [Flags]
        public enum SectorTypes : byte { Unallocated = 0x00, Bitmap = 0x01, Data = 0x02, Continuation = 0x04, Metadata = 0x08, Allocated = 0xFF }

        public delegate bool KeyCheck( byte[] buffer );

        string Filename;

        internal Stream TheFile;
        int Chunksize;

        internal BitmapSector Bits;
        int FreeSectorSearchStartIndex;

        internal const int RESERVED_SECTORS = 2;
        internal const int BITMAP_START_SECTOR = 0;
        internal const int METADATA_START_SECTOR = 1;

        internal const int LAST_SECTOR_IN_CHAIN = -1;

        bool OwnsStreamHandle;

        public Store( string filename, int defaultsectorsize )
        {
            OwnsStreamHandle = true;
            Filename = filename;
            var dest = new FileStream( Filename, FileMode.OpenOrCreate, FileAccess.ReadWrite );

            Initialize( dest, defaultsectorsize );
        }

        public Store( Stream dest, int defaultsectorsize )
        {
            OwnsStreamHandle = false;
            Filename = null;

            Initialize( dest, defaultsectorsize );
        }

        private void Initialize( Stream dest, int defaultsectorsize )
        {
            if ( defaultsectorsize > 0 && defaultsectorsize < 16 ) throw new Exception( "Minimum chunk size: 16 bytes." );

            Chunksize = defaultsectorsize <= 0 ? 1024 : defaultsectorsize;

            TheFile = dest;

            InitializeChunksize();
            Bits = new BitmapSector( this, BITMAP_START_SECTOR, Chunksize );
            FreeSectorSearchStartIndex = RESERVED_SECTORS;

            InitializeReservedSectors();
        }

        public const int SectorHeaderSize = sizeof( byte ) + sizeof( int );
        public const int FirstSectorHeaderSize = SectorHeaderSize + sizeof( long );

        public long FirstSectorDataSize
        {
            get
            {
                return Chunksize - FirstSectorHeaderSize;
            }
        }

        public long SectorDataSize
        {
            get
            {
                return Chunksize - SectorHeaderSize;
            }
        }

        internal long BitmapToPos( int bit )
        {
            return BitmapSector.BitmapToPos( bit, Chunksize );
        }

        internal long PosToBitmap( int bit )
        {
            return BitmapSector.PosToBitmap( bit, Chunksize );
        }

        private void InitializeChunksize()
        {
            if ( TheFile.Length < 4 )
            {
                if ( TheFile.CanWrite )
                {
                    TheFile.Position = 0;
                    TheFile.WriteInt32( Chunksize );
                }
                else
                {
                    throw new IOException( "Underlying stream have no Write functionality." );
                }
            }
            else
            {
                TheFile.Position = 0;
				Chunksize = StreamUtils.ReadInt32( TheFile );
            }
        }

        public void Flush()
        {
            TheFile.Flush();
        }

        private void InitializeReservedSectors()
        {
            if ( !Bits[BITMAP_START_SECTOR] )                 // Start bitmap sector
            {
                Bits[BITMAP_START_SECTOR] = true;
                TheFile.Position = BitmapToPos( BITMAP_START_SECTOR );
				TheFile.WriteUInt8( (byte)SectorTypes.Bitmap );
				TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
            }

            if ( !Bits[METADATA_START_SECTOR] )                 // Start key / metadata sector
            {
                Bits[METADATA_START_SECTOR] = true;
                TheFile.Position = BitmapToPos( METADATA_START_SECTOR );
				TheFile.WriteUInt8( (byte)SectorTypes.Metadata );
				TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
				TheFile.WriteUInt64( 0L ); // Might be size :P
            }
        }

        public long GetDataLength( int ix )
        {
            if ( !Bits[ix] ) return 0; // Free

            TheFile.Position = BitmapToPos( ix );

			var sectortype = StreamUtils.ReadInt8( TheFile );
            if ( (Store.SectorTypes)sectortype != Store.SectorTypes.Data ) return 0;

			var nextsector = StreamUtils.ReadInt32( TheFile );
			return StreamUtils.ReadInt64( TheFile );
        }

        internal int AllocateFreeSector()
        {
            int freesector;
            if ( !Bits.FindFreeSector( out freesector, 0 ) )
            {
                Bits.ExtendBitmapSpace();

                if ( !Bits.FindFreeSector( out freesector, 0 ) )
                {
                    throw new InternalBufferOverflowException( "You have found a bug in this class" );
                }
            }

            Bits[freesector] = true;
            FreeSectorSearchStartIndex = freesector + 1;

            TheFile.Position = BitmapToPos( freesector );
            TheFile.WriteByte( (byte)Store.SectorTypes.Allocated );

#if STORE_DETAILED_TRACE_LOGS
            System.Diagnostics.Debug.WriteLine( "AllocateFreeSector: " + freesector.ToString() );
#endif
            return freesector;
        }

        public int Next( int previx )
        {
            for ( int i = previx + 1; i < Bits.TotalBitCount; ++i )
            {
                if ( Bits[i] )
                {
                    TheFile.Position = BitmapToPos( i );
                    if ( (Store.SectorTypes)TheFile.ReadByte() == Store.SectorTypes.Data ) return i;
                }
            }

            return -1;
        }

        public void Delete( int ix )
        {
            if ( ix < RESERVED_SECTORS ) throw new Exception( "Cannot delete reserved indexes!" );
            if ( !Bits[ix] ) return;

            TheFile.Position = BitmapToPos( ix );

			var sectortype = (Store.SectorTypes)StreamUtils.ReadInt8( TheFile );
            if ( sectortype != Store.SectorTypes.Data ) throw new Exception( "Index is not pointing to a data index!" );

            DeleteFrom( ix );
        }

        /// <summary>
        /// Truncate the link of sectors starting with sector ix. The sector refering to sector ix (if any) will not be updated.
        /// </summary>
        /// <param name="ix"></param>
        protected void DeleteFrom( int ix )
        {
            Bits[ix] = false;
            if ( FreeSectorSearchStartIndex > ix ) FreeSectorSearchStartIndex = ix;

            TheFile.Position = BitmapToPos( ix );
            StreamUtils.WriteUInt8( TheFile, (byte)SectorTypes.Unallocated );
            var nextsector = StreamUtils.ReadInt32( TheFile );

            while ( nextsector != LAST_SECTOR_IN_CHAIN )
            {
                Bits[nextsector] = false;
                if ( FreeSectorSearchStartIndex > nextsector ) FreeSectorSearchStartIndex = nextsector;

                TheFile.Position = BitmapToPos( nextsector );
                StreamUtils.WriteUInt8( TheFile, (byte)SectorTypes.Unallocated );
                nextsector = StreamUtils.ReadInt32( TheFile );
            }
        }

        public void Delete( IEnumerable<int> ixs )
        {
            foreach ( var ix in ixs )
            {
                Delete( ix );
            }
        }

        public StoreStream IndexStream()
        {
            return IndexStream( 0L );
        }

        public StoreStream IndexStream( long offset )
        {
            var ix = AllocateFreeSector();

            TheFile.Position = BitmapToPos( ix );
            TheFile.WriteUInt8( (byte)Store.SectorTypes.Data );
            TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
            TheFile.WriteInt64( 0L );

            return new StoreStream( this, ix, offset );
        }

        public StoreStream IndexStream( bool reservefirstsector )
        {
            var ix = AllocateFreeSector();

            TheFile.Position = BitmapToPos( ix );
            TheFile.WriteUInt8( (byte)Store.SectorTypes.Data );
            TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
            TheFile.WriteInt64( 0L );

            return new StoreStream( this, ix, reservefirstsector );
        }

        public StoreStream IndexStream( int ix )
        {
            return new StoreStream( this, ix );
        }

        public StoreStream IndexStream( int ix, long offset )
        {
            return new StoreStream( this, ix, offset );
        }

        public StoreStream IndexStream( int ix, bool reservefirstsector )
        {
            return new StoreStream( this, ix, reservefirstsector );
        }

        public IList<KeyValuePair<int, byte[]>> ReadAll()
        {
            var result = new List<KeyValuePair<int,byte[]>>();

            var ix = RESERVED_SECTORS - 1;
            while ( ( ix = Next( ix ) ) > 0 )
            {
                result.Add( new KeyValuePair<int, byte[]>( ix, Read( ix ) ) );
            }

            return result;
        }

        public byte[] Read( int ix )
        {
            return Read( ix, -1 );
        }

        public byte[] Read( int ix, long maxlen )
        {
            if ( ix < RESERVED_SECTORS ) throw new Exception( "Reading of non-data sectors." );

            return ReadInternal( ix, maxlen );
        }

        public byte[] ReadMetadataIndex()
        {
            return ReadInternal( METADATA_START_SECTOR, -1 );
        }

        public byte[] ReadMetadataIndex( long maxlen )
        {
            return ReadInternal( METADATA_START_SECTOR, maxlen );
        }

        private byte[] ReadInternal( int ix, long maxlen )
        {
            TheFile.Position = BitmapToPos( ix );

			var sectortype = (Store.SectorTypes)StreamUtils.ReadInt8( TheFile );
            if ( sectortype != Store.SectorTypes.Data ) throw new Exception( "Trying to read in non data area" );
			var nextsector = StreamUtils.ReadInt32( TheFile );
			var totallen = StreamUtils.ReadInt64( TheFile );

            if ( maxlen > 0 ) totallen = Math.Min( totallen, maxlen );
            
            var result = new byte[totallen];
            var resultpos = 0;
            var buflen = FirstSectorDataSize;

            while ( resultpos < totallen )
            {
                var len = (int)Math.Min( totallen - resultpos, buflen );
				var readlen = TheFile.Read( result, resultpos, len );

                if ( nextsector == LAST_SECTOR_IN_CHAIN )
                {
                    var curlen = resultpos + readlen;
                    if ( totallen > curlen )
                    {
                        Logging.LogWarning( "Store: Warning! Stored length of the chain is faulty!" );
                        return result.Copy( 0, curlen );
                    }
                    break;
                }
                TheFile.Position = BitmapToPos( nextsector );

                var stype = (Store.SectorTypes)StreamUtils.ReadInt8( TheFile );
				if ( stype != Store.SectorTypes.Continuation ) throw new Exception( "Trying to read in non data area" );
				nextsector = StreamUtils.ReadInt32( TheFile );

                resultpos += readlen;
                buflen = SectorDataSize;
            }

            return result;
        }

        public void Write( byte[] data, int ix )
        {
            if ( ix < RESERVED_SECTORS ) throw new Exception( "Writing of non-data sectors." );
            WriteInternal( new BufLen[] { new BufLen( data ) }, ix );
        }

        public void Write( IEnumerable<BufLen> datasectors, int ix )
        {
            if ( ix < RESERVED_SECTORS ) throw new Exception( "Writing of non-data sectors." );
            WriteInternal( datasectors, ix );
        }

        private void WriteInternal( IEnumerable<BufLen> datablocks, int ix )
        {
            var thissector = ix;

            if ( !Bits[ix] ) throw new Exception( "Cannot update an unallocated sector!" );
            TheFile.Position = BitmapToPos( thissector );

			if ( (Store.SectorTypes)StreamUtils.ReadInt8( TheFile ) != Store.SectorTypes.Data ) throw new Exception( "Trying to write in non data area" );
			var nextsector = StreamUtils.ReadInt32( TheFile );
            TheFile.WriteInt64( datablocks.Sum( r => (long)r.Length ) );

            var sectorspaceleft = FirstSectorDataSize;

            foreach ( var datab in datablocks )
            {
                var data = (BufRefLen)datab;

                while ( data.Length > 0 )
                {
                    var len = (int)Math.Min( data.Length, sectorspaceleft );
                    TheFile.Write( data.BaseArray, data.BaseArrayOffset, len );

                    data.Seek( len );
                    sectorspaceleft -= len;

                    if ( data.Length == 0 ) break;

                    if ( sectorspaceleft == 0 )
                    {
                        if ( nextsector != LAST_SECTOR_IN_CHAIN )
                        {
                            if ( !Bits[nextsector] ) throw new Exception( "Cannot update an unallocated sector!" );
                            TheFile.Position = BitmapToPos( nextsector );

                            if ( (Store.SectorTypes)StreamUtils.ReadInt8( TheFile ) != Store.SectorTypes.Continuation ) 
                                throw new Exception( "Trying to update a sector outside of the allocated sector chain!" );

                            thissector = nextsector;
                            nextsector = StreamUtils.ReadInt32( TheFile );
                        }
                        else
                        {
                            thissector = ExtendLastSector( thissector );
                            nextsector = LAST_SECTOR_IN_CHAIN;
                        }

                        sectorspaceleft = SectorDataSize;
                    }
                }
            }
        }

        public int Write( byte[] data )
        {
            return WriteInternal( new BufLen[] { new BufLen( data ) } );
        }

        public int Write( IEnumerable<BufLen> datasectors )
        {
            return WriteInternal( datasectors );
        }

        private int WriteInternal( IEnumerable<BufLen> datablocks )
        {
            int result = 0;

            var thissector = AllocateFreeSector();

            TheFile.Position = BitmapToPos( thissector );
			TheFile.WriteUInt8( (byte)Store.SectorTypes.Data );
			TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
            TheFile.WriteInt64( datablocks.Sum( r => (long)r.Length ) );

            result = thissector;

            var sectorspaceleft = FirstSectorDataSize;

            foreach ( var datab in datablocks )
            {
                var data = (BufRefLen)datab;

                while ( data.Length > 0 )
                {
                    var len = (int)Math.Min( data.Length, sectorspaceleft );
                    TheFile.Write( data.BaseArray, data.BaseArrayOffset, len );

                    data.Seek( len );
                    sectorspaceleft -= len;

                    if ( data.Length == 0 ) break;

                    if ( sectorspaceleft == 0 )
                    {
                        var nextsector = ExtendLastSector( thissector );

                        thissector = nextsector;
                        sectorspaceleft = SectorDataSize;
                    }
                }
            }

            return result;
        }

        private int ExtendLastSector( int thissector )
        {
            var nextsector = AllocateFreeSector();

#if DEBUG
            TheFile.Position = BitmapToPos( thissector ) + 1;
            var ns = StreamUtils.ReadInt32( TheFile );
            if ( ns != LAST_SECTOR_IN_CHAIN ) throw new ArgumentException( "Store: Sector passed is not last in chain!" );
#endif
            TheFile.Position = BitmapToPos( thissector ) + 1;
            TheFile.WriteInt32( nextsector );

            TheFile.Position = BitmapToPos( nextsector );
            TheFile.WriteUInt8( (byte)Store.SectorTypes.Continuation );
            TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
            return nextsector;
        }

        public IList<KeyValuePair<int, byte[]>> GetMatching( KeyCheck eval, int bytes )
        {
            var result = new List<KeyValuePair<int, byte[]>>();

            var ix = RESERVED_SECTORS - 1;
            while ( ( ix = Next( ix ) ) > 0 )
            {
                var one = Read( ix, bytes );
                if ( eval( one ) )
                    result.Add( new KeyValuePair<int, byte[]>( ix, Read( ix ) ) );
            }

            return result;
        }

        public IList<int> GetMatchingIx( KeyCheck eval, int bytes )
        {
            var result = new List<int>();

            var ix = RESERVED_SECTORS - 1;
            while ( ( ix = Next( ix ) ) > 0 )
            {
                var one = Read( ix, bytes );
                if ( eval( one ) ) result.Add( ix );
            }

            return result;
        }


        #region IDisposable Members

        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        protected virtual void Dispose( bool disposing )
        {
            if ( disposing )
            {
                // free managed resources
                if ( TheFile != null )
                {
                    if ( OwnsStreamHandle ) TheFile.Dispose();
                    TheFile = null;
                }
            }
            // free native resources if there are any.

        }
        #endregion

        #region Stream
        public class StoreStream : Stream, IDisposable
        {
            private Store TheStore;
            private int Ix;

            List<int> Sectors = new List<int>();
            private long PositionOffset = 0L;

            public StoreStream( Store store, int ix, bool reseverfirstsector )
                : this( store, ix, reseverfirstsector ? store.FirstSectorDataSize : 0L )
            {
            }

            public StoreStream( Store store, int ix )
                : this( store, ix, 0L )
            {
            }

            public StoreStream( Store store, int ix, long offset )
            {
                TheStore = store;
                Ix = ix;

                if ( offset < 0 ) throw new ArgumentException( "offset must be >= 0!" );
                PositionOffset = offset;

                var file = TheStore.TheFile;

                // Get current state
                file.Position = TheStore.BitmapToPos( ix );

                var sectortype = (Store.SectorTypes)StreamUtils.ReadInt8( file );
                if ( sectortype != Store.SectorTypes.Data ) throw new Exception( "Trying to read in non data area" );
                var nextsector = StreamUtils.ReadInt32( file );
                var CurrentLength = StreamUtils.ReadInt64( file );

                while ( sectortype == SectorTypes.Data || sectortype == SectorTypes.Continuation )
                {
                    Sectors.Add( ix );

                    if ( nextsector == LAST_SECTOR_IN_CHAIN ) break;
                    file.Position = TheStore.BitmapToPos( nextsector );
                    ix = nextsector;

                    sectortype = (Store.SectorTypes)StreamUtils.ReadInt8( file );
                    if ( sectortype != Store.SectorTypes.Data && sectortype != SectorTypes.Continuation ) 
                        throw new Exception( "Trying to read in non data area" );
                    nextsector = StreamUtils.ReadInt32( file );
                }
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return true; }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            public override void Flush()
            {
                TheStore.Flush();
            }

            public int StoreIndex
            {
                get
                {
                    return Ix;
                }
            }

            internal int GetSectorFromPosition( long pos )
            {
                if ( ( pos + PositionOffset ) < TheStore.FirstSectorDataSize )
                {
                    return Sectors[0];
                }
                else
                {
                    return Sectors[1 + (int)Math.Floor( ( ( pos + PositionOffset ) - TheStore.FirstSectorDataSize ) / (float)TheStore.SectorDataSize )];
                }
            }

            private int CurrentSector
            {
                get
                {
                    return GetSectorFromPosition( CurrentPosition );
                }
            }

            /// <summary>
            /// Returns the offset of the stream data position relative the sector start.
            /// </summary>
            /// <param name="pos"></param>
            /// <returns></returns>
            internal int GetSectorOffsetFromPosition( long pos )
            {
                if ( ( pos + PositionOffset ) < TheStore.FirstSectorDataSize )
                {
                    return (int)( pos + PositionOffset ) + FirstSectorHeaderSize;
                }
                else
                {
                    return (int)( ( pos + PositionOffset - TheStore.FirstSectorDataSize ) % TheStore.SectorDataSize ) + SectorHeaderSize;
                }
            }

            private int CurrentSectorOffset
            {
                get
                {
                    return GetSectorOffsetFromPosition( CurrentPosition );
                }
            }

            private long CurrentLength = 0;
            public override long Length
            {
                get { return CurrentLength; }
            }

            private long CurrentPosition = 0;
            public override long Position
            {
                get
                {
                    return CurrentPosition;
                }
                set
                {
                    CurrentPosition = value;
                }
            }

            public override long Seek( long offset, SeekOrigin origin )
            {
                switch ( origin )
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;

                    case SeekOrigin.Current:
                        Position = Position + offset;
                        break;

                    case SeekOrigin.End:
                        Position = Length + offset - 1;
                        break;
                }

                return Position;
            }

            public override int Read( byte[] buffer, int offset, int maxlen )
            {
                var totallen = (int)Math.Min( buffer.Length - offset, Math.Min( Length - Position, maxlen ) );
                
                var resultpos = offset;
                var readlensum = 0L;

                while ( readlensum < totallen )
                {
                    while ( TheStore.Chunksize - CurrentSectorOffset > 0 && readlensum < totallen )
                    {
                        TheStore.TheFile.Position = TheStore.BitmapToPos( CurrentSector ) + CurrentSectorOffset;

                        var len = (int)Math.Min( TheStore.Chunksize - CurrentSectorOffset, totallen - readlensum );
                        var readlen = TheStore.TheFile.Read( buffer, resultpos, len );

                        Position += readlen;
                        resultpos += readlen;
                        readlensum += readlen;
                    }

                    TheStore.TheFile.Position = TheStore.BitmapToPos( CurrentSector ) + CurrentSectorOffset;
                }

                return totallen;
            }

            public override void Write( byte[] buffer, int offset, int count )
            {
                var writelen = Math.Min( count, buffer.Length - offset );
                var spaceneeded = Position + writelen;
                if ( spaceneeded > Length ) SetLength( spaceneeded );

                while ( writelen > 0 )
                {
                    TheStore.TheFile.Position = TheStore.BitmapToPos( CurrentSector ) + CurrentSectorOffset;

                    var len = Math.Min( TheStore.Chunksize - CurrentSectorOffset, writelen );
                    TheStore.TheFile.Write( buffer, offset, len );

                    offset += len;
                    writelen -= len;
                    Position += len;
                }
            }

            public override void SetLength( long value )
            {
                if ( value == Length ) return;

                if ( value > Length )
                {
                    while ( value > Length )
                    {
                        var newix = TheStore.AllocateFreeSector();
                        if ( newix < 0 ) throw new IOException( "Unable to grow Store" );
                        Sectors.Add( newix );

                        CurrentLength += ( Sectors.Count == 1 ) ? TheStore.FirstSectorDataSize : TheStore.SectorDataSize;

                        if ( Sectors.Count > 1 )
                        {
                            TheStore.TheFile.Position = TheStore.BitmapToPos( Sectors[Sectors.Count - 2] ) + 1;
                            TheStore.TheFile.WriteInt32( newix );
                        }

                        TheStore.TheFile.Position = TheStore.BitmapToPos( newix );
                        TheStore.TheFile.WriteUInt8( (byte)( Sectors.Count == 1 ? Store.SectorTypes.Data: SectorTypes.Continuation ) );
                        TheStore.TheFile.WriteInt32( LAST_SECTOR_IN_CHAIN );
                    }
                }
                else
                {
                    int sectorsneeded;

                    if ( value <= TheStore.FirstSectorDataSize )
                        sectorsneeded = 1;
                    else
                        sectorsneeded = (int)( ( value - TheStore.FirstSectorDataSize ) / TheStore.SectorDataSize + 1 );

                    if ( sectorsneeded < Sectors.Count )
                    {
                        TheStore.DeleteFrom( Sectors[sectorsneeded] );
                        while ( Sectors.Count > sectorsneeded ) Sectors.Remove( Sectors.Count - 1 );
                    }

                    CurrentLength = value;
                }

                TheStore.TheFile.Position = TheStore.BitmapToPos( Sectors[0] ) + SectorHeaderSize;
                TheStore.TheFile.WriteInt64( Length );
            }

            #region IDisposable Members

            public new void Dispose()
            {
                Dispose( true );
                base.Dispose();
                GC.SuppressFinalize( this );
            }

            protected override void Dispose( bool disposing )
            {
                if ( disposing )
                {
                    // free managed resources
                    Flush();
                    TheStore = null;
                    Sectors = null;
                }
                // free native resources if there are any.

            }
            #endregion
        }

        #endregion
    }
}
