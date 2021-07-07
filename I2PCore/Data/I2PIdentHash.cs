using System;
using Org.BouncyCastle.Utilities.Encoders;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PIdentHash: I2PType, IEquatable<I2PIdentHash>
    {
        public static readonly I2PIdentHash Zero = 
            new I2PIdentHash( new BufLen( new byte[32] ) );

        public readonly BufLen Hash;
        readonly int CachedHash;
        readonly string Id32ShortField;

        private I2PIdentHash( BufLen hash )
        {
            Hash = hash;
            CachedHash = Hash.GetHashCode();
            Id32ShortField = $"[{BufUtils.ToBase32String( Hash ).Substring( 0, 5 )}]";
        }

        static BufLen CreateRandomBuf( bool random )
        {
            var buf = new BufLen( new byte[32] );
            if ( random ) buf.Randomize();
            return buf;
        }

        public I2PIdentHash( bool random ): this( CreateRandomBuf( random ) )
        {
        }

        static BufLen CreateBase32ParsedBuf( string base32addr )
        {
            var st = base32addr;
            if ( st.EndsWith( ".i2p", StringComparison.Ordinal ) ) st = st.Substring( 0, st.Length - 4 );
            if ( st.EndsWith( ".b32", StringComparison.Ordinal ) ) st = st.Substring( 0, st.Length - 4 );
            var buf = new BufLen( BufUtils.Base32ToByteArray( st ) );
            return buf;
        }

        public I2PIdentHash( string base32addr ) : this( CreateBase32ParsedBuf( base32addr ) )
        {
        }

        public I2PIdentHash( BufRef buf ) : this( buf.ReadBufLen( 32 ) )
        {
        }

        static BufLen CreateKnCBuf( I2PKeysAndCert kns )
        {
            var ar = kns.ToByteArray();
            var buf = new BufLen( I2PHashSHA256.GetHash( ar, 0, ar.Length ) );
            return buf;
        }

        public I2PIdentHash( I2PKeysAndCert kns ): this( CreateKnCBuf( kns ) )
        {
        }

        public string Id32Short
        {
            get
            {
                return Id32ShortField;
            }
        }

        public string Id32
        {
            get
            {
                return BufUtils.ToBase32String( Hash );
            }
        }

        public string Id64
        {
            get
            {
                return System.Text.Encoding.ASCII.GetString( UrlBase64.Encode( Hash.ToByteArray() ) );
            }
        }

        I2PRoutingKey RoutingKeyCache;
        DateTime RoutingKeyCacheDay = DateTime.MinValue;
        public I2PRoutingKey RoutingKey
        {
            get
            {
                var daynow = DateTime.UtcNow.Date;
                if ( RoutingKeyCache != null && RoutingKeyCacheDay == daynow ) return RoutingKeyCache;
                
                RoutingKeyCache = new I2PRoutingKey( this );
                RoutingKeyCacheDay = daynow;

                return RoutingKeyCache;
            }
        }

        public BufLen Hash16
        {
            get
            {
                return new BufLen( Hash, 0, 16 );
            }
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( (BufRefLen)Hash );
        }

        public override string ToString()
        {
            return Id32;
        }

        public byte this[int ix]
        {
            get { return Hash[ix]; }
        }

        public static bool operator == ( I2PIdentHash left, I2PIdentHash right )
        {
            if ( left is null && right is null ) return true;
            if ( left is null || right is null ) return false;
            return left.Hash == right.Hash;
        }

        public static bool operator !=( I2PIdentHash left, I2PIdentHash right )
        {
            if ( left is null && right is null ) return false;
            if ( left is null || right is null ) return true;
            return left.Hash != right.Hash;
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            if ( !( obj is I2PIdentHash other ) ) return false;
            return Hash == other.Hash;
        }

        #region IEquatable<I2PIdentHash> Members
        public bool Equals( I2PIdentHash other )
        {
            if ( other is null ) return false;
            return Hash == other.Hash;
        }
        #endregion

        public override int GetHashCode()
        {
            return CachedHash;
        }

        public int CompareTo( I2PIdentHash other )
        {
            for ( int i = 0; i < 32; ++i )
            {
                if ( Hash[i] < other.Hash[i] ) return -1;
                if ( Hash[i] > other.Hash[i] ) return 1;
            }
            return 0;
        }

        public static bool operator <( I2PIdentHash left, I2PIdentHash right )
        {
            if ( left is null || right is null ) return false;
            for ( int i = 0; i < 32; ++i )
            {
                if ( left[i] < right[i] ) return true;
                if ( left[i] > right[i] ) return false;
            }
            return false;
        }

        public static bool operator >( I2PIdentHash left, I2PIdentHash right )
        {
            if ( left is null || right is null ) return false;
            for ( int i = 0; i < 32; ++i )
            {
                if ( left[i] < right[i] ) return false;
                if ( left[i] > right[i] ) return true;
            }
            return false;
        }
    }
}
