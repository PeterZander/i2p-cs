using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using I2PCore.Data;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace I2PCore.Utils
{
    public static class ElGamalCrypto
    {
        public const int ClearTextLength = 222;
        public const int EncryptedPaddedLength = 514;
        public const int EncryptedShortLength = 512;
        public const int EGBlockLength = 255;

        static readonly SecureRandom Rnd = new SecureRandom();

        public static byte[] Encrypt( BufLen data, I2PPublicKey key, bool zeropad )
        {
            var result = new byte[zeropad ? EncryptedPaddedLength : EncryptedShortLength];
            Encrypt( new BufRefLen( result ), data, key, zeropad );
            return result;
        }

        public static void Encrypt( BufRef dest, BufLen data, I2PPublicKey key, bool zeropad )
        {
            if ( data == null || data.Length > ClearTextLength )
            {
                throw new InvalidParameterException( $"ElGamal data must be {ClearTextLength} bytes or less!" );
            }

            var k = new BigInteger( I2PConstants.ElGamalFullExponentBits, Rnd );
            var a = I2PConstants.ElGamalG.ModPow( k, I2PConstants.ElGamalP );
            var b1 = key.ToBigInteger().ModPow( k, I2PConstants.ElGamalP );

            var start = new BufLen( new byte[EGBlockLength] );
            var writer = new BufRefLen( start, 1 );

            start[0] = 0xFF;

            writer.Write( I2PHashSHA256.GetHash( data ) );
            writer.Write( data );
            var egblock = new BufLen( start, 0, writer - start );
            var egint = egblock.ToBigInteger();

            var b = b1.Multiply( egint ).Mod( I2PConstants.ElGamalP );

            var targetlen = zeropad
                    ? EncryptedPaddedLength / 2
                    : EncryptedShortLength / 2;

            WriteToDest( dest, a, targetlen );
            WriteToDest( dest, b, targetlen );
        }

        private static void WriteToDest( BufRef dest, BigInteger v, int targetlen )
        {
            var vba = v.ToByteArray();
            if ( vba.Length < targetlen )
            {
                dest.Write( new byte[targetlen - vba.Length] );
            }

            if ( vba.Length > targetlen )
            {
                dest.Write( new BufLen( vba, vba.Length - targetlen ) );
            }
            else
            {
                dest.Write( vba );
            }
        }

        public static BufLen Decrypt( BufLen data, I2PPrivateKey pkey, bool zeropad )
        {
            if ( data == null || zeropad && data.Length != EncryptedPaddedLength )
            {
                throw new ArgumentException( $"ElGamal padded data ({data?.Length}) to decrypt must be exactly {EncryptedPaddedLength} bytes!" );
            }

            if ( !zeropad && data.Length != EncryptedShortLength )
            {
                throw new ArgumentException( $"ElGamal data ({data?.Length}) to decrypt must be exactly {EncryptedShortLength} bytes!" );
            }

            var x = I2PConstants.ElGamalPMinusOne.Subtract( pkey.ToBigInteger() );

            var reader = new BufRefLen( data );

            var readlen = zeropad
                        ? EncryptedPaddedLength / 2
                        : EncryptedShortLength / 2;

            var a = reader.ReadBigInteger( readlen );
            var b = reader.ReadBigInteger( readlen );

            var m2 = b.Multiply( a.ModPow( x, I2PConstants.ElGamalP ) );
            var m1 = m2.Mod( I2PConstants.ElGamalP );
            var m = m1.ToByteArrayUnsigned();
            var payload = new BufLen( m, 33, ClearTextLength );
            var hash = I2PHashSHA256.GetHash( payload );
            if ( !BufUtils.Equal( m, 1, hash, 0, 32 ) )
            {
                throw new ChecksumFailureException();
            }

            return payload;
        }
    }
}
