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
    public class ElGamalCrypto
    {
        BigInteger a;
        BigInteger b1;

        static readonly SecureRandom Rnd = new SecureRandom();

        public class HashCheckFailException: Exception
        {
        }

        public ElGamalCrypto( I2PPublicKey key )
        {
            var k = new BigInteger( I2PConstants.ElGamalP.BitLength, Rnd );
            if ( k.CompareTo( BigInteger.Zero ) == 0 ) k = BigInteger.One;

            a = I2PConstants.ElGamalG.ModPow( k, I2PConstants.ElGamalP );
            b1 = key.ToBigInteger().ModPow( k, I2PConstants.ElGamalP );
        }

        public byte[] Encrypt( BufLen data, bool zeropad )
        {
            var result = new byte[zeropad ? 514 : 512];
            Encrypt( new BufRefLen( result ), data, zeropad );
            return result;
        }

        public void Encrypt( BufRef dest, BufLen data, bool zeropad )
        {
            if ( data == null || data.Length > 222 )
            {
                throw new InvalidParameterException( "ElGamal data must be 222 bytes or less!" );
            }

            var start = new BufLen( new byte[255] );
            var writer = new BufRefLen( start, 1 );

            while ( true )
            {
                start[0] = BufUtils.RandomBytes( 1 )[0];
                if ( start[0] != 0 )
                {
                    break;
                }
            }

            writer.Write( I2PHashSHA256.GetHash( data ) );
            writer.Write( data );
            var egblock = new BufLen( start, 0, writer - start );

            var b = b1
                    .Multiply( 
                        new BigInteger( 
                                1,
                                egblock.BaseArray,
                                egblock.BaseArrayOffset,
                                egblock.Length ) )
                    .Mod( I2PConstants.ElGamalP );

            dest.Write( new BufLen( a.ToByteArray( 257 ), zeropad ? 0 : 1 ) );
            dest.Write( new BufLen( b.ToByteArray( 257 ), zeropad ? 0 : 1 ) );
        }

        public static BufLen Decrypt( BufLen data, I2PPrivateKey pkey, bool zeropad )
        {
            if ( data == null || zeropad && data.Length != 514 )
            {
                throw new ArgumentException( "ElGamal padded data to decrypt must be exactly 514 bytes!" );
            }

            if ( !zeropad && data.Length != 512 )
            {
                throw new ArgumentException( "ElGamal data to decrypt must be exactly 512 bytes!" );
            }

            var x = I2PConstants.ElGamalPMinusOne.Subtract( pkey.ToBigInteger() );

            var reader = new BufRefLen( data );
            var a = reader.ReadBigInteger( zeropad ? 257 : 256 );
            var b = reader.ReadBigInteger( zeropad ? 257 : 256 );

            var m2 = b.Multiply( a.ModPow( x, I2PConstants.ElGamalP ) );
            var m1 = m2.Mod( I2PConstants.ElGamalP );
            var m = m1.ToByteArrayUnsigned();
            var payload = new BufLen( m, 33, 222 );
            var hash = I2PHashSHA256.GetHash( payload );
            if ( !BufUtils.Equal( m, 1, hash, 0, 32 ) )
            {
                throw new HashCheckFailException();
            }

            return payload;
        }
    }
}
