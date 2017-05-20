 using System;
 using System.Collections.Generic;
 using System.Linq;
 using System.Text;
 using Org.BouncyCastle.Math;
 using I2PCore.Data;
 using Org.BouncyCastle.Security;
 
 namespace I2PCore.Utils
 {
     public class ElGamalCrypto
     {
         BigInteger a;
         BigInteger b1;
 
         public class HashCheckFailException: Exception
         {
         }

         public ElGamalCrypto( I2PPublicKey key )
         {
             var k = new BigInteger( I2PConstants.ElGamalP.BitLength, new SecureRandom() );
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
                 throw new InvalidParameterException( "ElGamal data length can max be 222 bytes!" );

             var hashbuf = new BufRefLen( new byte[255] );
 
             hashbuf.Write8( 0xFF );
             hashbuf.Write( I2PHashSHA256.GetHash( data ) );
             hashbuf.Write( data );
             hashbuf.Reset();

             var b = b1.Multiply( new BigInteger( 1, hashbuf.ToByteArray() ) ).Mod( I2PConstants.ElGamalP );
 
             if ( zeropad )
             {
                 dest.Write8( 0 );
                 dest.Write( a.ToByteArray( 256 ) );
                 dest.Write8( 0 );
                 dest.Write( b.ToByteArray( 256 ) );
             }
             else
             {
                 dest.Write( a.ToByteArray( 256 ) );
                 dest.Write( b.ToByteArray( 256 ) );
             }
         }
 
         public static BufLen Decrypt( BufLen data, I2PPrivateKey pkey, bool zeropad )
         {
             if ( data == null || ( zeropad && data.Length != 514 ) )
                 throw new ArgumentException( "ElGamal padded data to decrypt must be exactly 514 bytes!" );
             if ( !zeropad && data.Length != 512 )
                 throw new ArgumentException( "ElGamal data to decrypt must be exactly 512 bytes!" );

             var x = I2PConstants.ElGamalP.Subtract( pkey.ToBigInteger() ).Subtract( BigInteger.One );

             BigInteger a, b;
             var reader = new BufRefLen( data );
             if ( zeropad )
             {
                 reader.Seek( 1 );
                 a = reader.ReadBigInteger( 256 );
                 reader.Seek( 1 );
                 b = reader.ReadBigInteger( 256 );
             }
             else
             {
                 a = reader.ReadBigInteger( 256 );
                 b = reader.ReadBigInteger( 256 );
             }
 
             var m2 = b.Multiply( a.ModPow( x, I2PConstants.ElGamalP ) );
             var m1 = m2.Mod( I2PConstants.ElGamalP ); 
             var m = m1.ToByteArray( 255 );
             var hash = I2PHashSHA256.GetHash( m, 33, 222 );
             if ( !BufUtils.Equal( m, 1, hash, 0, 32 ) ) throw new HashCheckFailException();
 
             return new BufLen( m, 33, 222 );
         }
      }
 }
