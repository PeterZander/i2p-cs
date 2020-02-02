using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Transport.NTCP
{
    internal static class SessionRequest
    {
        internal static void Receive( DHHandshakeContext context, BufLen data )
        {
            var reader = new BufRefLen( data );

            context.XBuf = reader.ReadBufLen( 256 );
            context.X = new I2PPublicKey( new BufRefLen( context.XBuf ), I2PKeyType.DefaultAsymetricKeyCert );
            context.HXxorHI = reader.ReadBufLen( 32 );

            var HXxorHI = I2PHashSHA256.GetHash( context.XBuf );

            var idenhash = RouterContext.Inst.MyRouterIdentity.IdentHash;
            for ( int i = 0; i < HXxorHI.Length; ++i ) HXxorHI[i] ^= idenhash.Hash[i];

            if ( !context.HXxorHI.Equals( HXxorHI ) ) throw new ChecksumFailureException( "NTCP Incoming connection from " +
                context.Client.DebugId + " HXxorHI check failed." );
        }

        internal static byte[] Send( DHHandshakeContext context )
        {
            var dest = new byte[288];
            var writer = new BufRefLen( dest );

            var keys = I2PPrivateKey.GetNewKeyPair();
            context.PrivateKey = keys.PrivateKey;
            context.X = keys.PublicKey;
            context.XBuf = context.X.Key;

            context.HXxorHI = new BufLen( I2PHashSHA256.GetHash( context.XBuf ) );

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( 
                "SessionRequest: Remote cert: " + context.RemoteRI.Certificate.ToString() + ". XBuf len: " + context.XBuf.Length.ToString() );
#endif
            var idenhash = context.RemoteRI.IdentHash;
            for ( int i = 0; i < context.HXxorHI.Length; ++i ) context.HXxorHI[i] ^= idenhash.Hash[i];

            writer.Write( context.XBuf );
            writer.Write( context.HXxorHI );

            return dest;
        }
    }
}
