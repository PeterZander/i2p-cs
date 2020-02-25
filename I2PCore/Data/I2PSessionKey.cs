using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSessionKey : I2PType, IComparable<I2PSessionKey>, IEqualityComparer<I2PSessionKey>
    {
        public readonly BufLen Key;

        public I2PSessionKey()
        {
            Key = new BufLen( BufUtils.Random( 32 ) );
        }

        public I2PSessionKey( byte[] buf )
        {
            Key = new BufLen( buf, 0, 32 );
        }

        public I2PSessionKey( I2PSessionKey src )
        {
            Key = new BufLen( src.Key );
        }

        public I2PSessionKey( BufRef buf )
        {
            if ( buf is null )
            {
                throw new ArgumentException( "SessionKey must be 32 bytes" );
            }

            Key = buf.ReadBufLen( 32 );

            if ( Key.Length != 32 )
            {
                throw new ArgumentException( "SessionKey must be 32 bytes" );
            }
        }

        public I2PSessionKey( BufLen buf )
        {
            if ( buf is null || buf.Length != 32 )
            {
                throw new ArgumentException( "SessionKey must be 32 bytes" );
            }
            Key = buf;
        }

        public void Write( BufRefStream dest )
        {
            Key.WriteTo( dest );
        }

        public override bool Equals( object obj )
        {
            var sk = obj as I2PSessionKey;
            if ( obj is null || sk is null ) return false;
            return Key.Equals( sk.Key );
        }

        bool IEqualityComparer<I2PSessionKey>.Equals( I2PSessionKey x, I2PSessionKey y )
        {
            return x.Equals( y );
        }

        int IComparable<I2PSessionKey>.CompareTo( I2PSessionKey other )
        {
            return BufLen.Compare( Key, other.Key );
        }

        int IEqualityComparer<I2PSessionKey>.GetHashCode( I2PSessionKey obj )
        {
            return GetHashCode();
        }

        public static bool operator ==( I2PSessionKey left, I2PSessionKey right )
        {
            return Equals( left, right );
        }

        public static bool operator !=( I2PSessionKey left, I2PSessionKey right )
        {
            return !Equals( left, right );
        }

        public static bool operator >( I2PSessionKey left, I2PSessionKey right )
        {
            return BufLen.Compare( left.Key, right.Key ) > 0;
        }

        public static bool operator <( I2PSessionKey left, I2PSessionKey right )
        {
            return BufLen.Compare( left.Key, right.Key ) < 0;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        public override string ToString()
        {
            return $"{Key:h10}";
        }
    }
}
