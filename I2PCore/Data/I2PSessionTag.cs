using System;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSessionTag : I2PType, IEquatable<I2PSessionTag>
    {
        public const int TagLength = 32;

        // Session tag lifespan.
        // "The session tags delivered successfully are remembered for a brief period (15 minutes currently)"
        // https://geti2p.net/en/docs/how/elgamal-aes
        public static readonly TickSpan TagLifetime = TickSpan.Minutes( 15 );
        public readonly TickCounter Created = TickCounter.Now;

        public readonly BufLen Value;

        public I2PSessionTag()
        {
            Value = new BufLen( BufUtils.Random( TagLength ) );
        }

        public I2PSessionTag( BufRef buf )
        {
            Value = buf.ReadBufLen( TagLength );
        }

        public void Write( BufRefStream dest )
        {
            Value.WriteTo( dest );
        }

        public bool Equals( I2PSessionTag other )
        {
            if ( other is null ) return false;
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
