using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public abstract class I2PKeyType: I2PType
    {
        public BufLen Key;

        public enum KeyTypes : ushort
        {
            Invalid = ushort.MaxValue,
            NotImplemented = ushort.MaxValue - 1,
            ElGamal2048 = 0,
            P256 = 1,
            P384 = 2,
            P521 = 3,
            X25519 = 4,
        }

        // Replace when ElGamal is optional
        public static readonly I2PCertificate DefaultAsymetricKeyCert = new I2PCertificate();

        public static readonly I2PCertificate DefaultSigningKeyCert = new I2PCertificate();

        public readonly I2PCertificate Certificate;

        public int KeySizeBits { get { return KeySizeBytes * 8; } }
        public abstract int KeySizeBytes { get; }

        protected I2PKeyType( I2PCertificate cert )
        {
            Certificate = cert;
        }

        protected I2PKeyType( BufRef buf, I2PCertificate cert )
        {
            Certificate = cert;
            Key = buf.ReadBufLen( KeySizeBytes );
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( ToByteArray() );
        }

        public byte[] ToByteArray()
        {
            return Key.ToByteArray();
        }

        public BigInteger ToBigInteger()
        {
            return Key.ToBigInteger();
        }

        public override string ToString()
        {
            return $"I2PKeyType {GetType().Name}" +
                $"Key : {KeySizeBits} bits, {KeySizeBytes} bytes." +
                $"Key : {Key}";
        }
        public static int PublicKeyLength( I2PKeyType.KeyTypes kt )
        {
            switch ( kt )
            {
                case I2PKeyType.KeyTypes.ElGamal2048:
                    return 256;

                case I2PKeyType.KeyTypes.P256:
                    return 64;

                case I2PKeyType.KeyTypes.P384:
                    return 96;

                case I2PKeyType.KeyTypes.P521:
                    return 132;

                case I2PKeyType.KeyTypes.X25519:
                    return 32;

                default:
                    throw new NotImplementedException();
            }
        }
        public static int PrivateKeyLength( I2PKeyType.KeyTypes kt )
        {
            switch ( kt )
            {
                case I2PKeyType.KeyTypes.ElGamal2048:
                    return 256;

                case I2PKeyType.KeyTypes.P256:
                    return 32;

                case I2PKeyType.KeyTypes.P384:
                    return 48;

                case I2PKeyType.KeyTypes.P521:
                    return 66;

                case I2PKeyType.KeyTypes.X25519:
                    return 32;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
