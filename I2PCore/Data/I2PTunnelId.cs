using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PTunnelId : I2PType
    {
        public static readonly I2PTunnelId Zero = new I2PTunnelId( (uint)0 );

        /// <summary>
        /// Not flipped as it is just a tag.
        /// </summary>
        uint Id;

        public I2PTunnelId()
        {
            Id = BufUtils.RandomUint();
        }

        public I2PTunnelId( UInt32 id )
        {
            Id = id;
        }

        public I2PTunnelId( I2PTunnelId src )
        {
            Id = src.Id;
        }

        public I2PTunnelId( BufRef buf )
        {
            Id = buf.Read32();
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( BitConverter.GetBytes( Id ) );
        }

        public void Write( BufRef dest )
        {
            dest.Write32( Id );
        }

        public override string ToString()
        {
            return $"I2PTunnelId: {Id}";
        }

        public static bool operator ==( I2PTunnelId left, I2PTunnelId right )
        {
            if ( left is null && right is null ) return true;
            if ( left is null || right is null ) return false;
            return left.Id == right.Id;
        }

        public static bool operator !=( I2PTunnelId left, I2PTunnelId right )
        {
            if ( left is null && right is null ) return true;
            if ( left is null || right is null ) return false;
            return left.Id != right.Id;
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            if ( !( obj is I2PTunnelId ) ) return false;
            return this == (I2PTunnelId)obj;
        }

        public override int GetHashCode()
        {
            return (int)Id;
        }

        public static implicit operator uint( I2PTunnelId tid )
        {
            return tid.Id;
        }

        public static implicit operator I2PTunnelId( uint tid )
        {
            return new I2PTunnelId( tid );
        }
    }
}
