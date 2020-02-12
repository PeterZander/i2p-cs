using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Transport.NTCP
{
    internal static class SessionConfirmB
    {
        internal static void Receive( DHHandshakeContext context, I2PKeysAndCert ri )
        {
            var responselength = ri.Certificate.SignatureLength;
            responselength += BufUtils.Get16BytePadding( responselength );

            var data = context.Client.BlockReceive( responselength );
            context.Dectryptor.ProcessBytes( data );

            var signature = new I2PSignature( new BufRefLen( data ), context.RemoteRI.Certificate );

            if ( !I2PSignature.SupportedSignatureType( context.RemoteRI.Certificate.SignatureType ) )
                throw new SignatureCheckFailureException( "NTCP SessionConfirmB recv not supported signature type: " +
                    context.RemoteRI.Certificate.SignatureType.ToString() );

            var ok = I2PSignature.DoVerify( context.RemoteRI.SigningPublicKey, signature,
                context.X.Key,
                context.Y.Key,
                RouterContext.Inst.MyRouterIdentity.IdentHash.Hash,
                BufUtils.Flip32BL( context.TimestampA ),
                BufUtils.Flip32BL( context.TimestampB ) );

            Logging.LogTransport( $"SessionConfirmB: {context.RemoteRI.Certificate.SignatureType} signature check: {ok}" );

            if ( !ok ) throw new SignatureCheckFailureException( "NTCP SessionConfirmB recv sig check failure" );
        }

        internal static byte[] Send( DHHandshakeContext context )
        {
            var msglen = RouterContext.Inst.MyRouterIdentity.Certificate.SignatureLength;
            msglen += BufUtils.Get16BytePadding( msglen );

            var writer = new BufRefLen( new byte[msglen] );

            var SigBuf = I2PSignature.DoSign( RouterContext.Inst.PrivateSigningKey,
                    context.XBuf,
                    context.YBuf,
                    context.RemoteRI.IdentHash.Hash,
                    (BufLen)BufUtils.Flip32( context.TimestampA ),
                    (BufLen)BufUtils.Flip32( context.TimestampB ) );

            writer.Write( SigBuf );
            writer.Write( BufUtils.Random( writer.Length ) );

            writer.Reset();
            context.Encryptor.ProcessBytes( (BufLen)writer );

            return writer.ToByteArray();
        }
    }
}
