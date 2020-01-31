using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Router;
using System.Threading;
using System.Net.Sockets;

namespace I2PCore.Transport.NTCP
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
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SessionConfirmA send TimestampA: " + ( I2PDate.RefDate.AddSeconds( context.TimestampA ).ToString() ) );
            DebugUtils.Log( "SessionConfirmA send TimestampB: " + ( I2PDate.RefDate.AddSeconds( context.TimestampB ).ToString() ) );
#endif

            var sign = I2PSignature.DoSign( RouterContext.Inst.PrivateSigningKey,
                context.X.Key,
                context.Y.Key,
                context.RemoteRI.IdentHash.Hash,
                BufUtils.Flip32BL( context.TimestampA ),
                BufUtils.Flip32BL( context.TimestampB ) );

            var padsize = BufUtils.Get16BytePadding( (int)( sign.Length + cleartext.Length ) );
            cleartext.Write( BufUtils.Random( padsize ) );

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
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SessionConfirmA recv TimestampA: " + ( I2PDate.RefDate.AddSeconds( context.TimestampA ).ToString() ) );
            DebugUtils.Log( "SessionConfirmA recv TimestampB: " + ( I2PDate.RefDate.AddSeconds( context.TimestampB ).ToString() ) );
#endif

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
#if LOG_ALL_TRANSPORT
                DebugUtils.Log( "SessionConfirmA recv not enough data: " + datastart.Length.ToString() + ". I want " + needbytes.ToString() + " bytes." );
#endif
                var buf = context.Client.BlockReceive( needbytes - gotbytes );
                writer.Write( buf );
            }

            if ( needbytes - datastart.Length > 0 )
            {
                context.Dectryptor.ProcessBytes( new BufLen( datastart, datastart.Length, needbytes - datastart.Length ) );
            }

            var signature = new I2PSignature( new BufRefLen( sigstart ), context.RemoteRI.Certificate );

            if ( !I2PSignature.SupportedSignatureType( context.RemoteRI.Certificate.SignatureType ) )
                throw new SignatureCheckFailureException( "NTCP SessionConfirmA recv not supported signature type: " + 
                    context.RemoteRI.Certificate.SignatureType.ToString() );

            var sigok = I2PSignature.DoVerify(
                context.RemoteRI.SigningPublicKey,
                signature,
                context.XBuf,
                context.YBuf,
                RouterContext.Inst.MyRouterIdentity.IdentHash.Hash,
                new BufLen( BufUtils.Flip32B( context.TimestampA ) ),
                new BufLen( BufUtils.Flip32B( context.TimestampB ) )
            );

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SessionConfirmA recv: " + context.RemoteRI.Certificate.SignatureType.ToString() + 
                " signature check: " + sigok.ToString() + "." );
#endif
            if ( !sigok ) throw new SignatureCheckFailureException( "NTCP SessionConfirmA recv signature check fail" );
        }

    }
}
