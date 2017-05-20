using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Digests;
using I2PCore;
using I2PCore.Transport.NTCP;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Transport.NTCP
{
    internal static class SessionCreated
    {
        internal static void Receive( DHHandshakeContext context, BufLen data )
        {
            var reader = new BufRefLen( data );

            context.Y = new I2PPublicKey( reader, context.RemoteRI.Certificate );
            context.YBuf = context.Y.Key;

            var sharedkey = BufUtils.DHI2PToByteArray( context.Y.ToBigInteger().ModPow( context.PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

            context.SessionKey = new I2PSessionKey( sharedkey );

            var key = new KeyParameter( context.SessionKey.Key.ToByteArray() );

            var enciv = new BufLen( context.HXxorHI.BaseArray, context.HXxorHI.Length - 16, 16 );
            var deciv = new BufLen( context.YBuf, context.YBuf.Length - 16, 16 );

            context.Encryptor = new CbcBlockCipher( new AesEngine() );
            context.Encryptor.Init( true, new ParametersWithIV( key, enciv.BaseArray, enciv.BaseArrayOffset, enciv.Length ) );

            context.Dectryptor = new CbcBlockCipher( new AesEngine() );
            context.Dectryptor.Init( false, new ParametersWithIV( key, deciv.BaseArray, deciv.BaseArrayOffset, deciv.Length ) );

            var encrbuf = new BufLen( reader, 0, 32 + 4 + 12 );
            context.Dectryptor.ProcessBytes( encrbuf );

            context.HXY = reader.ReadBufLen( 32 );
            context.TimestampB = reader.ReadFlip32();

            var checkhash = I2PHashSHA256.GetHash( context.XBuf, context.YBuf );
            if ( !context.HXY.Equals( checkhash ) ) throw new ChecksumFailureException( "NTCP SessionCreated received HXY check failed!" );
        }

        internal static byte[] Send( DHHandshakeContext context )
        {
            var clear = new byte[304];
            var writer = new BufRefLen( clear );

            var keys = I2PPrivateKey.GetNewKeyPair();
            context.PrivateKey = keys.PrivateKey;
            context.Y = keys.PublicKey;
            context.YBuf = new BufLen( context.Y.Key );

            var sharedkey = BufUtils.DHI2PToByteArray( context.X.ToBigInteger().ModPow( context.PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );
            context.SessionKey = new I2PSessionKey( sharedkey );

            writer.Write( context.YBuf );

            context.TimestampB = (uint)( DateTime.UtcNow - I2PDate.RefDate ).TotalSeconds;

            writer.Write( I2PHashSHA256.GetHash( context.XBuf, context.YBuf ) );
            writer.WriteFlip32( context.TimestampB );
            writer.Write( BufUtils.Random( 12 ) );

            var key = new KeyParameter( context.SessionKey.Key.ToByteArray() );

            var iv = context.YBuf.PeekB( context.YBuf.Length - 16, 16 );

            context.Encryptor = new CbcBlockCipher( new AesEngine() );
            context.Encryptor.Init( true, new ParametersWithIV( key, iv, 0, 16 ) );

            iv = context.HXxorHI.PeekB( context.HXxorHI.Length - 16, 16 );

            context.Dectryptor = new CbcBlockCipher( new AesEngine() );
            context.Dectryptor.Init( false, new ParametersWithIV( key, iv, 0, 16 ) );

            context.Encryptor.ProcessBytes( new BufLen( clear, 256, 48 ) );

            return clear;
        }
    }
}
