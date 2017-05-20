using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{

    class HashedItemGroup : IEquatable<HashedItemGroup>
    {
        int Hash;

        public HashedItemGroup( params object[] objs )
        {
            int result = 0;
            foreach ( var obj in objs ) if ( obj != null )
                {
                    result ^= obj.GetHashCode();
                }
            Hash = result;
        }

        public bool Equals( HashedItemGroup other )
        {
            if ( other == null ) return false;
            return other.Hash == Hash;
        }

        public override bool Equals( object obj )
        {
            if ( obj == null ) return false;
            var other = (HashedItemGroup)obj;
            if ( other == null ) return false;
            return other.Hash == Hash;
        }

        public override int GetHashCode()
        {
            return Hash;
        }
    }

    class OrderedHashedItemGroup : HashedItemGroup
    {
        int Hash;

        public OrderedHashedItemGroup( params object[] objs )
        {
            int result = 0;
            foreach ( var obj in objs ) if ( obj != null )
                {
                    result = ( result << 1 ) | ( result >> -1 );
                    result ^= obj.GetHashCode();
                }
            Hash = result;
        }
    }
}
