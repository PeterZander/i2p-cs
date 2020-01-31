using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSessionTag : I2PType, IEquatable<I2PSessionTag>
    {
        // Session tag lifespan.
        // "The session tags delivered successfully are remembered for a brief period (15 minutes currently)"
        // https://geti2p.net/en/docs/how/elgamal-aes
        public const int TagLifetimeMinutes = 15;

        public const int TagLength = 32;

        public readonly BufLen Value;

        public readonly TickCounter Created = TickCounter.Now;

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
            if ( other == null ) return false;
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
