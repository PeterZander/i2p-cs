using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Security;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PIdentHash: I2PType, IEquatable<I2PIdentHash>
    {
        public static readonly I2PIdentHash Zero = new I2PIdentHash();

        public readonly BufLen Hash;
        readonly int CachedHash;

        private I2PIdentHash()
        {
            Hash = new BufLen( new byte[32] );
            CachedHash = Hash.GetHashCode();
        }

        public I2PIdentHash( bool random )
        {
            Hash = new BufLen( new byte[32] );
            if ( random ) Hash.Randomize();
            CachedHash = Hash.GetHashCode();
        }

        public I2PIdentHash( string base32addr )
        {
            var st = base32addr;
            if ( st.EndsWith( ".i2p" ) ) st = st.Substring( 0, st.Length - 4 );
            if ( st.EndsWith( ".b32" ) ) st = st.Substring( 0, st.Length - 4 );
            Hash = new BufLen( BufUtils.Base32ToByteArray( st ) );
            CachedHash = Hash.GetHashCode();
        }

        public I2PIdentHash( BufRef buf )
        {
            Hash = buf.ReadBufLen( 32 );
            CachedHash = Hash.GetHashCode();
        }

        public I2PIdentHash( I2PKeysAndCert kns )
        {
            var ar = kns.ToByteArray();
            Hash = new BufLen( I2PHashSHA256.GetHash( ar, 0, ar.Length ) );
            CachedHash = Hash.GetHashCode();
        }

        public string Id32Short
        {
            get
            {
                return "[" + BufUtils.ToBase32String( Hash ).Substring( 0, 5 ) + "]";
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
        int RoutingKeyCacheDay = -1;
        public I2PRoutingKey RoutingKey
        {
            get
            {
                var daynow = DateTime.UtcNow.Day;
                if ( RoutingKeyCache != null && RoutingKeyCacheDay == daynow ) return RoutingKeyCache;
                RoutingKeyCacheDay = daynow;
                RoutingKeyCache = new I2PRoutingKey( this, false );
                return RoutingKeyCache;
            }
        }

        I2PRoutingKey NextRoutingKeyCache;
        int NextRoutingKeyCacheDay = -1;
        public I2PRoutingKey NextRoutingKey
        {
            get
            {
                var daynow = DateTime.UtcNow.AddDays( 1 ).Day;
                if ( NextRoutingKeyCache != null && NextRoutingKeyCacheDay == daynow ) return NextRoutingKeyCache;
                NextRoutingKeyCacheDay = daynow;
                NextRoutingKeyCache = new I2PRoutingKey( this, true );
                return NextRoutingKeyCache;
            }
        }

        public BufLen Hash16
        {
            get
            {
                return new BufLen( Hash, 0, 16 );
            }
        }

        public void Write( List<byte> dest )
        {
            dest.AddRange( Hash );
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
            if ( ReferenceEquals( left, null ) && ReferenceEquals( right, null ) ) return true;
            if ( ReferenceEquals( left, null ) || ReferenceEquals( right, null ) ) return false;
            return left.Hash == right.Hash;
        }

        public static bool operator !=( I2PIdentHash left, I2PIdentHash right )
        {
            if ( ReferenceEquals( left, null ) && ReferenceEquals( right, null ) ) return false;
            if ( ReferenceEquals( left, null ) || ReferenceEquals( right, null ) ) return true;
            return left.Hash != right.Hash;
        }

        public override bool Equals( object obj )
        {
            if ( ReferenceEquals( obj, null ) ) return false;
            if ( !( obj is I2PIdentHash ) ) return false;
            return this == (I2PIdentHash)obj;
        }

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
            if ( ReferenceEquals( left, null ) || ReferenceEquals( right, null ) ) return false;
            for ( int i = 0; i < 32; ++i )
            {
                if ( left[i] < right[i] ) return true;
                if ( left[i] > right[i] ) return false;
            }
            return false;
        }

        public static bool operator >( I2PIdentHash left, I2PIdentHash right )
        {
            if ( ReferenceEquals( left, null ) || ReferenceEquals( right, null ) ) return false;
            for ( int i = 0; i < 32; ++i )
            {
                if ( left[i] < right[i] ) return false;
                if ( left[i] > right[i] ) return true;
            }
            return false;
        }

        #region IEquatable<I2PIdentHash> Members

        public bool Equals( I2PIdentHash other )
        {
            return this == other;
        }

        #endregion
    }
}
