using System;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.SessionLayer;

namespace I2PCore.TransportLayer.NTCP
{
    internal static class SessionConfirmA
    {
        internal static BufLen Send( DHHandshakeContext context )
        {
            context.TimestampA = (uint)Math.Ceiling( ( DateTime.UtcNow - I2PDate.RefDate ).TotalSeconds );

            var cleartext = new BufRefStream();
            var ri = RouterContext.Inst.MyRouterIdentity.ToByteArray();
            cleartext.Write( BufUtils.Flip16B( (ushort)ri.Length ) );
            cleartext.Write( ri );

            cleartext.Write( BufUtils.Flip32B( context.TimestampA ) );

            Logging.LogDebugData( $"SessionConfirmA send TimestampA: {I2PDate.RefDate.AddSeconds( context.TimestampA )}" );
            Logging.LogDebugData( $"SessionConfirmA send TimestampB: {I2PDate.RefDate.AddSeconds( context.TimestampB )}" );

            var sign = I2PSignature.DoSign( RouterContext.Inst.PrivateSigningKey,
                context.X.Key,
                context.Y.Key,
                context.RemoteRI.IdentHash.Hash,
                BufUtils.Flip32BL( context.TimestampA ),
                BufUtils.Flip32BL( context.TimestampB ) );

            var padsize = BufUtils.Get16BytePadding( (int)( sign.Length + cleartext.Length ) );
            cleartext.Write( BufUtils.RandomBytes( padsize ) );

            cleartext.Write( sign );

            var buf = new BufLen( cleartext.ToArray() );
            context.Encryptor.ProcessBytes( buf );

            return buf;
        }

        internal static void Receive( DHHandshakeContext context, BufLen datastart )
        {
            var origbuf = new BufRefLen( datastart );
            var reader = new BufRefLen( datastart );

            context.Dectryptor.ProcessBytes( datastart );

            var rilen = reader.ReadFlip16();
            var ribuf = reader.ReadBufRefLen( rilen );
            context.TimestampA = reader.ReadFlip32();

            Logging.LogDebugData( $"SessionConfirmA recv TimestampA: {I2PDate.RefDate.AddSeconds( context.TimestampA )}" );
            Logging.LogDebugData( $"SessionConfirmA recv TimestampB: {I2PDate.RefDate.AddSeconds( context.TimestampB )}" );

            context.RemoteRI = new I2PRouterIdentity( ribuf );
            context.RunContext.RemoteRouterIdentity = context.RemoteRI;

            var sizeofpayload = 2 + rilen + 4 + context.RemoteRI.Certificate.SignatureLength;
            var paddingsize = BufUtils.Get16BytePadding( sizeofpayload );
            reader.Seek( paddingsize );

            var sigstart = new BufLen( reader, 0, context.RemoteRI.Certificate.SignatureLength );

            var needbytes = 2 + context.RemoteRI.Certificate.RouterIdentitySize + 4 + context.RemoteRI.Certificate.SignatureLength;
            needbytes += BufUtils.Get16BytePadding( needbytes );

            var writer = new BufRef( origbuf, origbuf.Length );
            var gotbytes = writer - origbuf;
            
            if ( gotbytes < needbytes )
            {
                Logging.LogDebugData( $"SessionConfirmA recv not enough data: {datastart.Length}. I want {needbytes} bytes." );

                var buf = context.Client.BlockReceive( needbytes - gotbytes );
                writer.Write( buf );
            }

            if ( needbytes - datastart.Length > 0 )
            {
                context.Dectryptor.ProcessBytes( new BufLen( datastart, datastart.Length, needbytes - datastart.Length ) );
            }

            var signature = new I2PSignature( new BufRefLen( sigstart ), context.RemoteRI.Certificate );

            var sigok = I2PSignature.DoVerify(
                context.RemoteRI.SigningPublicKey,
                signature,
                context.XBuf,
                context.YBuf,
                RouterContext.Inst.MyRouterIdentity.IdentHash.Hash,
                new BufLen( BufUtils.Flip32B( context.TimestampA ) ),
                new BufLen( BufUtils.Flip32B( context.TimestampB ) )
            );

            Logging.LogTransport( $"SessionConfirmA recv: {context.RemoteRI.Certificate.SignatureType} " + 
                $"signature check: {sigok}." );

            if ( !sigok ) throw new SignatureCheckFailureException( "NTCP SessionConfirmA recv signature check fail" );
        }

    }
}
