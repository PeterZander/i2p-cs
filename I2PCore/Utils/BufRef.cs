using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;

namespace I2PCore.Utils
{
    /// <summary>
    /// Buffer reference with fixed offset.
    /// </summary>
    public class BufBase: IEnumerable<byte>, IEquatable<BufBase>, IComparable<BufBase>, IFormattable
    {
        protected readonly byte[] Data; 
        protected readonly int StartIx; // Original start position. Will never change.
        protected int Position;

        public BufBase( byte[] data ) { Data = data; }
        public BufBase( byte[] data, int offset ) { Data = data; StartIx = Position = offset; }
        public BufBase( BufBase src ) { Data = src.Data; StartIx = src.StartIx; Position = src.Position; }
        public BufBase( BufBase src, int offset ) { Data = src.Data; StartIx = Position = src.Position + offset; }

        /// <summary>
        /// Not taking offset in buffer into account.
        /// </summary>
        public byte[] BaseArray { get { return Data; } }

        /// <summary>
        /// Current offset in BaseArray.
        /// </summary>
        public int BaseArrayOffset { get { return Position; } }

        public static bool SameBuffer( BufBase left, BufBase right )
        {
            return ReferenceEquals( left.Data, right.Data );
        }

        public byte this[int ix]
        {
            get
            {

                return Data[ix + Position];
            }
            set
            {
                Data[ix + Position] = value;
            }
        }

        protected virtual void PreReadCheck( int length )
        {
        }

        protected virtual void PreWriteCheck( int length )
        {
        }

        public ulong PeekFlip64( int offset )
        {
            PreReadCheck( 8 );

            ulong result;
            result = (ulong)Data[Position + offset] << 56;
            result |= (ulong)Data[Position + 1 + offset] << 48;
            result |= (ulong)Data[Position + 2 + offset] << 40;
            result |= (ulong)Data[Position + 3 + offset] << 32;
            result |= (ulong)Data[Position + 4 + offset] << 24;
            result |= (ulong)Data[Position + 5 + offset] << 16;
            result |= (ulong)Data[Position + 6 + offset] << 8;
            result |= (ulong)Data[Position + 7 + offset];
            return result;
        }

        public uint PeekFlip32( int offset )
        {
            PreReadCheck( 4 );

            uint result;
            result = (uint)Data[Position + offset] << 24;
            result |= (uint)Data[Position + 1 + offset] << 16;
            result |= (uint)Data[Position + 2 + offset] << 8;
            result |= (uint)Data[Position + 3 + offset];
            return result;
        }

        public ushort PeekFlip16( int offset )
        {
            PreReadCheck( 2 );

            ushort result;
            result = (ushort)( Data[Position + offset] << 8 );
            result |= Data[Position + 1 + offset];
            return result;
        }

        public byte Peek8( int offset )
        {
            PreReadCheck( 1 );

            return Data[Position + offset];
        }

        public ulong Peek64( int offset )
        {
            PreReadCheck( 8 );

            ulong result;
            result = Data[Position + offset];
            result |= ( (ulong)Data[Position + 1 + offset] ) << 8;
            result |= ( (ulong)Data[Position + 2 + offset] ) << 16;
            result |= ( (ulong)Data[Position + 3 + offset] ) << 24;
            result |= ( (ulong)Data[Position + 4 + offset] ) << 32;
            result |= ( (ulong)Data[Position + 5 + offset] ) << 40;
            result |= ( (ulong)Data[Position + 6 + offset] ) << 48;
            result |= ( (ulong)Data[Position + 7 + offset] ) << 56;
            return result;
        }

        public uint Peek32( int offset )
        {
            PreReadCheck( 4 );

            uint result;
            result = Data[Position + offset];
            result |= ( (uint)( Data[Position + 1 + offset] ) ) << 8;
            result |= ( (uint)( Data[Position + 2 + offset] ) ) << 16;
            result |= ( (uint)( Data[Position + 3 + offset] ) ) << 24;
            return result;
        }

        public ushort Peek16( int offset )
        {
            PreReadCheck( 2 );

            ushort result;
            result = Data[Position + offset];
            result |= (ushort)( ( Data[Position + 1 + offset] ) << 8 );
            return result;
        }

        public void Peek( byte[] val, int offset )
        {
            PreReadCheck( offset + val.Length );

            Array.Copy( Data, Position + offset, val, 0, val.Length );
        }

        public void Peek( byte[] val, int valoffset, int offset, int length )
        {
            PreReadCheck( offset + length );

            Array.Copy( Data, Position + offset, val, valoffset, length );
        }

        public byte[] PeekB( int offset, int length )
        {
            PreReadCheck( offset + length );

            var result = new byte[length];
            Array.Copy( Data, Position + offset, result, 0, length );
            return result;
        }

        public void Poke64( ulong val, int offset )
        {
            PreWriteCheck( 8 );

            Data[Position + offset] = (byte)( val & 0xFF );
            Data[Position + 1 + offset] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position + 2 + offset] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position + 3 + offset] = (byte)( ( val >> 24 ) & 0xFF );
            Data[Position + 4 + offset] = (byte)( ( val >> 32 ) & 0xFF );
            Data[Position + 5 + offset] = (byte)( ( val >> 40 ) & 0xFF );
            Data[Position + 6 + offset] = (byte)( ( val >> 48 ) & 0xFF );
            Data[Position + 7 + offset] = (byte)( val >> 56 );
        }

        public void Poke32( uint val, int offset )
        {
            PreWriteCheck( 4 );

            Data[Position + offset] = (byte)( val & 0xFF );
            Data[Position + 1 + offset] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position + 2 + offset] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position + 3 + offset] = (byte)( val >> 24 );
        }

        public void Poke16( ushort val, int offset )
        {
            PreWriteCheck( 2 );

            Data[Position + offset] = (byte)( val & 0xFF );
            Data[Position + 1 + offset] = (byte)( val >> 8 );
        }

        public void Poke8( byte val, int offset )
        {
            PreWriteCheck( offset + 1 );

            Data[Position + offset] = val;
        }

        public void Poke( byte[] val, int offset )
        {
            PreWriteCheck( offset + val.Length );

            Array.Copy( val, 0, Data, Position + offset, val.Length );
        }

        public void Poke( byte[] val, int offset, int maxlen )
        {
            var len = Math.Min( maxlen, val.Length );
            PreWriteCheck( offset + len );

            Array.Copy( val, 0, Data, Position + offset, len );
        }

        public void PokeFlip64( ulong val, int offset )
        {
            PreWriteCheck( offset + 8 );

            Data[Position + offset] = (byte)( ( val >> 56 ) & 0xFF );
            Data[Position + 1 + offset] = (byte)( ( val >> 48 ) & 0xFF );
            Data[Position + 2 + offset] = (byte)( ( val >> 40 ) & 0xFF );
            Data[Position + 3 + offset] = (byte)( ( val >> 32 ) & 0xFF );
            Data[Position + 4 + offset] = (byte)( ( val >> 24 ) & 0xFF );
            Data[Position + 5 + offset] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position + 6 + offset] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position + 7 + offset] = (byte)( val & 0xFF );
        }

        public void PokeFlip32( uint val, int offset )
        {
            PreWriteCheck( offset + 4 );

            Data[Position + offset] = (byte)( ( val >> 24 ) & 0xFF );
            Data[Position + 1 + offset] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position + 2 + offset] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position + 3 + offset] = (byte)( val & 0xFF );
        }

        public void PokeFlip16( ushort val, int offset )
        {
            PreWriteCheck( offset + 2 );

            Data[Position + offset] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position + 1 + offset] = (byte)( val & 0xFF );
        }

        public override string ToString()
        {
            return ToString( "8", null );
        }

        #region IEnumerable<byte> Members

        IEnumerator<byte> IEnumerable<byte>.GetEnumerator()
        {
            for ( int i = Position; i < Data.Length; ++i )
            {
                yield return Data[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for ( int i = Position; i < Data.Length; ++i )
            {
                yield return Data[i];
            }
        }

        #endregion

        #region IEquatable<BufBase> Members

        bool IEquatable<BufBase>.Equals( BufBase other )
        {
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            var other = obj as BufBase;
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public virtual bool Equals( byte[] other )
        {
            if ( other is null ) return false;

            var len = BaseArray.Length - BaseArrayOffset;
            if ( len != other.Length ) return false;

            for ( int i = 0; i < len; ++i )
                if ( BaseArray[BaseArrayOffset + i] != other[i] ) return false;
            return true;
        }

        public override int GetHashCode()
        {
            return ComputeHash( this );
        }

        public static bool Equal( BufBase b1, BufBase b2 )
        {
            if ( b1 is null && b2 is null ) return true;
            if ( b1 is null || b2 is null ) return false;
            if ( Object.ReferenceEquals( b1, b2 ) ) return true;

            var b1len = b1.BaseArray.Length - b1.BaseArrayOffset;
            var b2len = b2.BaseArray.Length - b2.BaseArrayOffset;
            if ( b1len != b2len ) return false;

            for ( int i = 0; i < b1len; ++i )
                if ( b1.BaseArray[b1.BaseArrayOffset + i] != b2.BaseArray[b2.BaseArrayOffset + i] ) return false;
            return true;
        }

        public static int Compare( BufBase b1, BufBase b2 )
        {
            if ( b1 is null && b2 is null ) return 0;
            if ( b1 is null ) return -1;
            if ( b2 is null ) return 1;
            if ( Object.ReferenceEquals( b1, b2 ) ) return 0;

            var b1len = b1.BaseArray.Length - b1.BaseArrayOffset;
            var b2len = b2.BaseArray.Length - b2.BaseArrayOffset;

            for ( int i = 0; i < b1len; ++i )
            {
                if ( b2.BaseArrayOffset + i >= b2.BaseArray.Length ) return 1;
                var c = b1.BaseArray[b1.BaseArrayOffset + i] - b2.BaseArray[b2.BaseArrayOffset + i];
                if ( c != 0 ) return Math.Sign( c );
            }

            if ( b2len > b1len ) return -1;
            return 0;
        }

        int IComparable<BufBase>.CompareTo( BufBase other )
        {
            return Compare( this, other );
        }

        public static bool operator ==( BufBase left, BufBase right )
        {
            return Equal( left, right );
        }

        public static bool operator !=( BufBase left, BufBase right )
        {
            return !Equal( left, right );
        }

        public static bool operator >( BufBase left, BufBase right )
        {
            return Compare( left, right ) > 0;
        }

        public static bool operator <( BufBase left, BufBase right )
        {
            return Compare( left, right ) < 0;
        }

        public static int ComputeHash( BufBase data )
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for ( int i = data.BaseArrayOffset; i < data.BaseArray.Length; ++i )
                    hash = ( hash ^ data.BaseArray[i] ) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }

        public string ToString( string format, IFormatProvider formatprovider )
        {
            int? len = null;

            if ( this is BufRefLen ) len = ( (BufRefLen)this ).Length;
            if ( this is BufLen ) len = ( (BufLen)this ).Length;
            if ( !len.HasValue ) len = Data.Length - Position;

            var result = new StringBuilder();

            bool showheader = true;
            bool showchars = false;
            bool showindex = false;
            bool parsingheader;

            do
            {
                parsingheader = false;

                if ( format?.StartsWith( "I", StringComparison.Ordinal ) ?? false )
                {
                    showindex = true;
                    format = format.Substring( 1 );
                    parsingheader = true;
                }
                if ( format?.StartsWith( "C", StringComparison.Ordinal ) ?? false )
                {
                    showchars = true;
                    format = format.Substring( 1 );
                    parsingheader = true;
                }
                if ( format?.StartsWith( "h", StringComparison.Ordinal ) ?? false )
                {
                    showheader = false;
                    format = format.Substring( 1 );
                    parsingheader = true;
                }
            } while ( parsingheader );

            if ( !int.TryParse( format, out var maxlen ) )
            {
                maxlen = 8;
            }

            if ( showheader )
            {
                result.Append( $"{GetType().Name} [{StartIx}:{Position}], Length: {len} [" );
            }
            else
            {
                result.Append( "[" );
            }

            for ( int i = 0; i < len && i < maxlen; ++i )
            {
                var bufbaseval = showindex
                        ? showchars
                            ? $"[{i}]0x{this[i]:X2}'{(char)this[i]}'"
                            : $"[{i}]0x{this[i]:X2}"
                        : showchars
                            ? $"0x{this[i]:X2}'{(char)this[i]}'"
                            : $"0x{this[i]:X2}";
                result.Append( i == 0 ? $"{bufbaseval}" : $",{bufbaseval}" );
            }
            result.Append( "]" );

            return result.ToString();
        }

        #endregion

    }

    /// <summary>
    /// Buffer reference with offset that can move while serializing.
    /// </summary>
    public class BufRef : BufBase
    {
        public BufRef( byte[] data ) : base( data, 0 ) { }
        public BufRef( byte[] data, int startix ) : base( data, startix ) { }
        public BufRef( BufBase src ) : base( src ) { }
        public BufRef( BufBase src, int offset ) : base( src, offset ) { }

        public void Reset() { Position = StartIx; }

        public int Seek( int offset ) { Position += offset; return Position; }

        /// <summary>
        /// Allocating a new array with the subset.
        /// </summary>
        public byte[] ToArray { get { return Data.Copy( Position, Data.Length - Position ); } }

        public ulong ReadFlip64()
        {
            PreReadCheck( 8 );

            ulong result;
            result = (ulong)Data[Position++] << 56;
            result |= (ulong)Data[Position++] << 48;
            result |= (ulong)Data[Position++] << 40;
            result |= (ulong)Data[Position++] << 32;
            result |= (ulong)Data[Position++] << 24;
            result |= (ulong)Data[Position++] << 16;
            result |= (ulong)Data[Position++] << 8;
            result |= (ulong)Data[Position++];
            return result;
        }

        public uint ReadFlip32()
        {
            PreReadCheck( 4 );

            uint result;
            result = (uint)Data[Position++] << 24;
            result |= (uint)Data[Position++] << 16;
            result |= (uint)Data[Position++] << 8;
            result |= (uint)Data[Position++];
            return result;
        }

        public ushort ReadFlip16()
        {
            PreReadCheck( 2 );

            ushort result;
            result = (ushort)( Data[Position++] << 8 );
            result |= Data[Position++];
            return result;
        }

        public byte Read8()
        {
            PreReadCheck( 1 );

            return Data[Position++];
        }

        public ulong Read64()
        {
            PreReadCheck( 8 );

            ulong result;
            result = (ulong)Data[Position++];
            result |= (ulong)Data[Position++] << 8;
            result |= (ulong)Data[Position++] << 16;
            result |= (ulong)Data[Position++] << 24;
            result |= (ulong)Data[Position++] << 32;
            result |= (ulong)Data[Position++] << 40;
            result |= (ulong)Data[Position++] << 48;
            result |= (ulong)Data[Position++] << 56;
            return result;
        }

        public uint Read32()
        {
            PreReadCheck( 4 );

            uint result;
            result = (uint)Data[Position++];
            result |= (uint)Data[Position++] << 8;
            result |= (uint)Data[Position++] << 16;
            result |= (uint)Data[Position++] << 24;
            return result;
        }

        public ushort Read16()
        {
            PreReadCheck( 2 );

            ushort result;
            result = (ushort)Data[Position++];
            result = (ushort)( Data[Position++] << 8 );
            return result;
        }

        public byte[] Read( int length )
        {
            PreReadCheck( length );

            var result = Data.Copy( Position, length );
            Position += length;
            return result;
        }

        public int Read( byte[] dest, int offset, int length )
        {
            PreReadCheck( length );

            Array.Copy( Data, Position, dest, offset, length );
            Position += length;
            return length;
        }

        public BigInteger ReadBigInteger( int length )
        {
            PreReadCheck( length );

            var result = new BigInteger( 1, Data, Position, length );
            Seek( length );
            return result;
        }

        public BufRef ReadBufRef( int length )
        {
            PreReadCheck( length );

            var result = new BufRef( this, 0 );
            Seek( length );
            return result;
        }

        public BufLen ReadBufLen( int length )
        {
            PreReadCheck( length );

            var result = new BufLen( this, 0, length );
            Seek( length );
            return result;
        }

        public BufRefLen ReadBufRefLen( int length )
        {
            PreReadCheck( length );

            var result = new BufRefLen( this, 0, length );
            Seek( length );
            return result;
        }

        public void WriteFlip64( ulong val )
        {
            PreWriteCheck( 8 );

            Data[Position++] = (byte)( ( val >> 56 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 48 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 40 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 32 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 24 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position++] = (byte)( val & 0xFF );
        }

        public void WriteFlip32( uint val )
        {
            PreWriteCheck( 4 );

            Data[Position++] = (byte)( val >> 24 );
            Data[Position++] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position++] = (byte)( val & 0xFF );
        }

        public void WriteFlip16( ushort val )
        {
            PreWriteCheck( 2 );

            Data[Position++] = (byte)( val >> 8 );
            Data[Position++] = (byte)( val & 0xFF );
        }

        public void Write8( byte val )
        {
            PreWriteCheck( 1 );

            Data[Position++] = val;
        }

        public void Write64( ulong val )
        {
            PreWriteCheck( 8 );

            Data[Position++] = (byte)( val & 0xFF );
            Data[Position++] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 24 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 32 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 40 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 48 ) & 0xFF );
            Data[Position++] = (byte)( val >> 56 );
        }

        public void Write32( uint val )
        {
            PreWriteCheck( 4 );

            Data[Position++] = (byte)( val & 0xFF );
            Data[Position++] = (byte)( ( val >> 8 ) & 0xFF );
            Data[Position++] = (byte)( ( val >> 16 ) & 0xFF );
            Data[Position++] = (byte)( val >> 24 );
        }

        public void Write16( ushort val )
        {
            PreWriteCheck( 2 );

            Data[Position++] = (byte)( val & 0xFF );
            Data[Position++] = (byte)( val >> 8 );
        }

        public void Poke( BufLen src )
        {
            PreWriteCheck( src.Length );

            Array.Copy( src.BaseArray, src.BaseArrayOffset, Data, Position, src.Length );
        }

        public void Poke( BufLen src, int offset )
        {
            PreWriteCheck( offset + src.Length );

            Array.Copy( src.BaseArray, src.BaseArrayOffset, Data, Position + offset, src.Length );
        }

        public void Poke( BufRefLen src, int offset )
        {
            PreWriteCheck( offset + src.Length );

            Array.Copy( src.Data, src.Position, Data, Position + offset, src.Length );
        }

        public void Poke( BufRefLen src, int offset, int maxlen )
        {
            var len = Math.Min( maxlen, src.Length );
            PreWriteCheck( offset + len );

            Array.Copy( src.Data, src.Position, Data, Position + offset, len );
        }

        public virtual int Write( byte[] src )
        {
            if ( src.Length == 0 ) return 0;

            PreWriteCheck( src.Length );

            Array.Copy( src, 0, BaseArray, BaseArrayOffset, src.Length );
            Seek( src.Length );
            return src.Length;
        }

        public virtual int Write( BufRefLen src )
        {
            PreWriteCheck( src.Length );

            Array.Copy( src.BaseArray, src.BaseArrayOffset, BaseArray, BaseArrayOffset, src.Length );
            Seek( src.Length );
            src.Seek( src.Length );
            return src.Length;
        }

        public virtual int Write( BufLen src )
        {
            PreWriteCheck( src.Length );

            Array.Copy( src.BaseArray, src.BaseArrayOffset, BaseArray, BaseArrayOffset, src.Length );
            Seek( src.Length );
            return src.Length;
        }

        public static int operator -( BufRef left, BufRef right )
        {
            if ( !BufBase.SameBuffer( left, right ) ) throw new InvalidOperationException( "Can only subtract BufRef with the same underlying buffer!" );
            return left.Position - right.Position;
        }

        public static int operator -( BufRef left, BufLen right )
        {
            if ( !BufBase.SameBuffer( left, right ) ) throw new InvalidOperationException( "Can only subtract BufRef with the same underlying buffer!" );
            return left.Position - right.BaseArrayOffset;
        }

        public static BufRef operator +( BufRef left, int right )
        {
            return new BufRef( left, right );
        }
    }

    /// <summary>
    /// Buffer reference with fixed offset and length.
    /// </summary>
    public class BufLen : BufBase, IEnumerable<byte>, IList<byte>, IEquatable<BufLen>, IComparable<BufLen>
    {
        public BufLen( byte[] data ) : base( data ) { LengthDef = data.Length; }
        public BufLen( byte[] data, int startix ) : base( data, startix ) { LengthDef = data.Length - startix; }
        public BufLen( byte[] data, int startix, int len ) : base( data, startix ) { LengthDef = len; }

        public BufLen( BufBase src, int offset, int len )
            : base( src, offset )
        {
            LengthDef = Math.Min( len, src.BaseArray.Length - src.BaseArrayOffset - offset );
        }

        public BufLen( BufLen src ) : base( src ) { LengthDef = src.LengthDef; }

        public BufLen( BufLen src, int offset )
            : base( src, offset ) 
        {
            LengthDef = src.LengthDef - offset;
        }

        public BufLen( BufLen src, int offset, int len ) : base( src, offset ) 
        {
            LengthDef = Math.Min( len, src.LengthDef - offset );
        }

        public BufLen( BufRefLen src ) : base( src ) { LengthDef = src.Length; }

        public BufLen( BufRefLen src, int offset ) : base( src, offset )
        {
            LengthDef = src.Length - offset;
        }

        public BufLen( BufRefLen src, int offset, int len ) : base( src, offset )
        {
            LengthDef = Math.Min( len, src.Length - offset );
        }

        readonly int LengthDef;

        public int Length { get { return LengthDef; } }

        public BufLen Clone() { return Clone( BaseArray, BaseArrayOffset, Length ); }

        public static BufLen Clone( byte[] buf, int offset, int length ) 
        {
            var copy = new byte[length];
            Array.Copy( buf, offset, copy, 0, length );
            return new BufLen( copy ); 
        }

        public string ToEncoding( Encoding enc )
        {
            return enc.GetString(
                BaseArray,
                BaseArrayOffset,
                Length );
        }

#if DEBUG
        protected override void PreReadCheck( int length )
        {
            if ( length > LengthDef )
            {
                throw new ArgumentException( "Reading outside of definied buffer space!\r\n" + System.Environment.StackTrace );
            }
        }

        protected override void PreWriteCheck( int length )
        {
            if ( length > LengthDef )
            {
                throw new ArgumentException( "Writing outside of definied buffer space!\r\n" + System.Environment.StackTrace );
            }
        }
#endif

        public void Poke( BufLen src, int offset )
        {
            Array.Copy( src.BaseArray, src.BaseArrayOffset, Data, Position + offset, src.Length );
        }

        public void Poke( BufLen src, int offset, int maxlen )
        {
            Array.Copy( src.BaseArray, src.BaseArrayOffset, Data, Position + offset, Math.Min( maxlen, src.Length ) );
        }

        public void WriteTo( BufRefStream dest )
        {
            dest.Write( Data, Position, LengthDef );
        }

        public void WriteTo( byte[] dest, int offset )
        {
            Array.Copy( Data, Position, dest, offset, Length );
        }

        public byte[] ToByteArray()
        {
            if ( StartIx == 0 && Position == 0 && Length == Data.Length ) return Data;
            return PeekB( 0, Length );
        }

        #region Operators
        public static explicit operator BufRef( BufLen buf )
        {
            return new BufRef( buf, 0 );
        }

        public static explicit operator BufRefLen( BufLen buf )
        {
            return new BufRefLen( buf, 0, buf.Length );
        }

        public static explicit operator BufLen( int value )
        {
            var ar = BitConverter.GetBytes( value );
            return new BufLen( ar, 0, ar.Length );
        }

        public static explicit operator BufLen( uint value )
        {
            var ar = BitConverter.GetBytes( value );
            return new BufLen( ar, 0, ar.Length );
        }

        public static explicit operator BufLen( ushort value )
        {
            var ar = BitConverter.GetBytes( value );
            return new BufLen( ar, 0, ar.Length );
        }

        public static explicit operator BufLen( byte value )
        {
            return new BufLen( new byte[] { value } );
        }
        #endregion

        #region IEnumerable<byte> Members

        IEnumerator<byte> IEnumerable<byte>.GetEnumerator()
        {
            for ( int i = Position; i < StartIx + LengthDef; ++i )
            {
                yield return Data[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for ( int i = Position; i < StartIx + LengthDef; ++i )
            {
                yield return Data[i];
            }
        }

        #endregion

        public override string ToString()
        {
            return ToString( "8", null );
        }

        #region IBufStream Members

        int IList<byte>.IndexOf( byte item )
        {
            for ( int i = 0; i < Length; ++i ) if ( this[i] == item ) return i;
            return -1;
        }

        void IList<byte>.Insert( int index, byte item )
        {
            throw new NotImplementedException();
        }

        void IList<byte>.RemoveAt( int index )
        {
            throw new NotImplementedException();
        }

        #endregion

        #region ICollection<byte> Members

        void ICollection<byte>.Add( byte item )
        {
            throw new NotImplementedException();
        }

        void ICollection<byte>.Clear()
        {
            throw new NotImplementedException();
        }

        bool ICollection<byte>.Contains( byte item )
        {
            for ( int i = 0; i < Length; ++i ) if ( this[i] == item ) return true;
            return false;
        }

        void ICollection<byte>.CopyTo( byte[] array, int arrayIndex )
        {
            Array.Copy( Data, Position, array, arrayIndex, Length );
        }

        int ICollection<byte>.Count
        {
            get { return Length; }
        }

        bool ICollection<byte>.IsReadOnly
        {
            get { return false; }
        }

        bool ICollection<byte>.Remove( byte item )
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IEquatable<BufLen> Members

        bool IEquatable<BufLen>.Equals( BufLen other )
        {
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            var other = obj as BufLen;
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public override bool Equals( byte[] other )
        {
            if ( other is null ) return false;

            if ( Length != other.Length ) return false;

            for ( int i = 0; i < Length; ++i )
                if ( BaseArray[BaseArrayOffset + i] != other[i] ) return false;
            return true;
        }

        public override int GetHashCode()
        {
            return ComputeHash( this );
        }

        public static bool Equal( BufLen b1, BufLen b2 )
        {
            if ( b1 is null && b2 is null ) return true;
            if ( b1 is null || b2 is null ) return false;
            if ( Object.ReferenceEquals( b1, b2 ) ) return true;
            if ( b1.Length != b2.Length ) return false;

            for ( int i = 0; i < b1.Length; ++i )
                if ( b1.BaseArray[b1.BaseArrayOffset + i] != b2.BaseArray[b2.BaseArrayOffset + i] ) return false;
            return true;
        }

        public static int Compare( BufLen b1, BufLen b2 )
        {
            if ( b1 is null && b2 is null ) return 0;
            if ( b1 is null ) return -1;
            if ( b2 is null ) return 1;
            if ( Object.ReferenceEquals( b1, b2 ) ) return 0;

            for ( int i = 0; i < b1.Length; ++i )
            {
                if ( i >= b2.Length ) return 1;
                var c = b1.BaseArray[b1.BaseArrayOffset + i] - b2.BaseArray[b2.BaseArrayOffset + i];
                if ( c != 0 ) return Math.Sign( c );
            }

            if ( b2.Length > b1.Length ) return -1;
            return 0;
        }

        int IComparable<BufLen>.CompareTo( BufLen other )
        {
            return Compare( this, other );
        }

        public static bool operator ==( BufLen left, BufLen right )
        {
            return Equal( left, right );
        }

        public static bool operator !=( BufLen left, BufLen right )
        {
            return !Equal( left, right );
        }

        public static bool operator >( BufLen left, BufLen right )
        {
            return Compare( left, right ) > 0;
        }

        public static bool operator <( BufLen left, BufLen right )
        {
            return Compare( left, right ) < 0;
        }

        public static int ComputeHash( BufLen data )
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                var end = data.BaseArrayOffset + data.Length;
                for ( int i = data.BaseArrayOffset; i < end; ++i )
                    hash = ( hash ^ data.BaseArray[i] ) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }

        #endregion
    }

    /// <summary>
    /// Buffer reference with movable offset and dynamic length.
    /// </summary>
    public class BufRefLen : BufRef, IEnumerable<byte>, IEquatable<BufRefLen>, IComparable<BufRefLen>
    {
        public BufRefLen( byte[] data ) : base( data ) { LengthDef = data.Length; }
        public BufRefLen( byte[] data, int startix ) : base( data, startix ) { LengthDef = data.Length - startix; }
        public BufRefLen( byte[] data, int startix, int len ) : base( data, startix ) { LengthDef = len; }

        public BufRefLen( BufBase src ) : base( src ) 
        { 
            LengthDef = src.BaseArray.Length - src.BaseArrayOffset; 
        }

        public BufRefLen( BufBase src, int offset ) : base( src, offset ) 
        { 
            LengthDef = src.BaseArray.Length - src.BaseArrayOffset - offset; 
        }

        public BufRefLen( BufBase src, int offset, int len ) : base( src, offset )
        {
            LengthDef = Math.Min( len, src.BaseArray.Length - src.BaseArrayOffset - offset );
        }

        public BufRefLen( BufRefLen src ) : base( src ) { LengthDef = src.LengthDef; }
        
        public BufRefLen( BufRefLen src, int offset ) : base( src, offset ) 
        {
            LengthDef = src.LengthDef - offset;
        }

        public BufRefLen( BufRefLen src, int offset, int len ) : base( src, offset ) 
        {
            LengthDef = Math.Min( len, src.BaseArray.Length - src.BaseArrayOffset - offset );
        }

        public BufRefLen( BufLen src ): base( src )
        {
            LengthDef = src.Length;
        }

        public BufRefLen( BufLen src, int offset ) : base( src, offset )
        {
            LengthDef = src.Length - offset;
        }

        public BufRefLen( BufLen src, int offset, int len ) : base( src, offset )
        {
            LengthDef = Math.Min( len, src.Length - offset );
        }

        readonly int LengthDef;

        public BufLen View { get { return new BufLen( Data, Position, Length ); } }

        public int Length { get { return LengthDef - ( Position - StartIx ); } }

        public BufRefLen Clone() { return Clone( BaseArray, BaseArrayOffset, Length ); }

        public static BufRefLen Clone( byte[] buf, int offset, int length )
        {
            var copy = new byte[length];
            Array.Copy( buf, offset, copy, 0, length );
            return new BufRefLen( copy );
        }

#if DEBUG
        protected override void PreReadCheck( int length )
        {
            if ( length > Length )
            {
                throw new ArgumentException( "Reading outside of definied buffer space!\r\n" + System.Environment.StackTrace );
            }
        }

        protected override void PreWriteCheck( int length )
        {
            if ( length > Length )
            {
                throw new ArgumentException( "Writing outside of definied buffer space!\r\n" + System.Environment.StackTrace );
            }
        }
#endif

        public void WriteTo( List<byte> dest )
        {
            dest.AddRange( Data.Skip( Position ).Take( LengthDef ) );
        }

        public void WriteTo( byte[] dest, int offset )
        {
            Array.Copy( Data, Position, dest, offset, Length );
        }

        public byte[] ToByteArray()
        {
            if ( StartIx == 0 && Position == 0 && Length == Data.Length ) return Data;
            return PeekB( 0, Length );
        }

        public static explicit operator BufLen( BufRefLen buf )
        {
            return new BufLen( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
        }

        #region IEnumerable<byte> Members

        IEnumerator<byte> IEnumerable<byte>.GetEnumerator()
        {
            for ( int i = Position; i < StartIx + LengthDef; ++i )
            {
                yield return Data[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for ( int i = Position; i < StartIx + LengthDef; ++i )
            {
                yield return Data[i];
            }
        }

        #endregion

        public override string ToString()
        {
            return ToString( "8", null );
        }

        #region IEquatable<BufRefLen> Members

        bool IEquatable<BufRefLen>.Equals( BufRefLen other )
        {
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            var other = obj as BufRefLen;
            if ( other is null ) return false;
            return Equal( this, other );
        }

        public override bool Equals( byte[] other )
        {
            if ( other is null ) return false;

            if ( Length != other.Length ) return false;

            for ( int i = 0; i < Length; ++i )
                if ( BaseArray[BaseArrayOffset + i] != other[i] ) return false;
            return true;
        }

        public override int GetHashCode()
        {
            return ComputeHash( this );
        }

        public static bool Equal( BufRefLen b1, BufRefLen b2 )
        {
            if ( b1 is null && b2 is null ) return true;
            if ( b1 is null || b2 is null ) return false;
            if ( Object.ReferenceEquals( b1, b2 ) ) return true;
            if ( b1.Length != b2.Length ) return false;

            for ( int i = 0; i < b1.Length; ++i )
                if ( b1.BaseArray[b1.BaseArrayOffset + i] != b2.BaseArray[b2.BaseArrayOffset + i] ) return false;
            return true;
        }

        public static int Compare( BufRefLen b1, BufRefLen b2 )
        {
            if ( b1 is null && b2 is null ) return 0;
            if ( b1 is null ) return -1;
            if ( b2 is null ) return 1;
            if ( Object.ReferenceEquals( b1, b2 ) ) return 0;

            for ( int i = 0; i < b1.Length; ++i )
            {
                if ( i >= b2.Length ) return 1;
                var c = b1.BaseArray[b1.BaseArrayOffset + i] - b2.BaseArray[b2.BaseArrayOffset + i];
                if ( c != 0 ) return Math.Sign( c );
            }

            if ( b2.Length > b1.Length ) return -1;
            return 0;
        }

        int IComparable<BufRefLen>.CompareTo( BufRefLen other )
        {
            return Compare( this, other );
        }

        public static bool operator ==( BufRefLen left, BufRefLen right )
        {
            return Equal( left, right );
        }

        public static bool operator !=( BufRefLen left, BufRefLen right )
        {
            return !Equal( left, right );
        }

        public static bool operator >( BufRefLen left, BufRefLen right )
        {
            return Compare( left, right ) > 0;
        }

        public static bool operator <( BufRefLen left, BufRefLen right )
        {
            return Compare( left, right ) < 0;
        }

        public static int ComputeHash( BufRefLen data )
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                var end = data.BaseArrayOffset + data.Length;
                for ( int i = data.BaseArrayOffset; i < end; ++i )
                    hash = ( hash ^ data.BaseArray[i] ) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }

        #endregion
    }

}
