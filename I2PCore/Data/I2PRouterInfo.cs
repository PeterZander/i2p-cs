using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Utilities.Encoders;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PRouterInfo : I2PType
    {
        public I2PRouterIdentity Identity;
        public I2PDate PublishedDate;
        public I2PRouterAddress[] Adresses;
        public I2PMapping Options;
        public I2PSignature Signature;

        BufLen Data;

        public I2PRouterInfo(
            I2PRouterIdentity identity,
            I2PDate publisheddate,
            I2PRouterAddress[] adresses,
            I2PMapping options,
            I2PSigningPrivateKey privskey )
        {
            Identity = identity;
            PublishedDate = publisheddate;
            Adresses = adresses;
            Options = options;

            var dest = new BufRefStream();
            Identity.Write( dest );
            PublishedDate.Write( dest );
            dest.Write( (byte)Adresses.Length );
            foreach ( var addr in Adresses )
            {
                addr.Write( dest );
            }
            dest.Write( 0 ); // Always zero
            Options.Write( dest );
            Data = new BufLen( dest.ToArray() );

            Signature = new I2PSignature( new BufRefLen( I2PSignature.DoSign( privskey, Data ) ), privskey.Certificate );
        }

        public I2PRouterInfo( BufRef reader, bool verifysig )
        {
            var startview = new BufRef( reader );

            Identity = new I2PRouterIdentity( reader );
            PublishedDate = new I2PDate( reader );

            int addrcount = reader.Read8();
            var addresses = new List<I2PRouterAddress>();
            for ( int i = 0; i < addrcount; ++i )
            {
                addresses.Add( new I2PRouterAddress( reader ) );
            }
            Adresses = addresses.ToArray();

            reader.Seek( reader.Read8() * 32 ); // peer_size. Unused.

            Options = new I2PMapping( reader );
            var payloadend = new BufRef( reader );

            Data = new BufLen( startview, 0, reader - startview );
            Signature = new I2PSignature( reader, Identity.Certificate );

            if ( verifysig )
            {
                var versig = VerifySignature();
                if ( !versig )
                {
                    throw new InvalidOperationException( "I2PRouterInfo signature check failed" );
                }
            }
        }

        public bool VerifySignature()
        {
            var versig = I2PSignature.SupportedSignatureType( Identity.Certificate.SignatureType );

            if ( !versig )
            {
                Logging.LogDebug( "RouterInfo: VerifySignature false. Not supported: " + Identity.Certificate.SignatureType.ToString() );
                return false;
            }

            versig = I2PSignature.DoVerify( Identity.SigningPublicKey, Signature, Data );
            if ( !versig )
            {
                Logging.LogDebug( "RouterInfo: I2PSignature.DoVerify failed: " + Identity.Certificate.SignatureType.ToString() );
                return false;
            }

            return true;
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
            Signature.Write( dest );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PRouterInfo" );

            result.AppendLine( "Identity     : " + Identity.IdentHash.Id32 );
            result.AppendLine( "Identity     : " + Identity.ToString() );
            result.AppendLine( "Publish date : " + PublishedDate.ToString() );

            foreach( var addr in Adresses )
            {
                result.AppendLine( "Address      : " + addr.ToString() );
            }

            result.AppendLine( Options.ToString() );
            if ( Signature == null )
            {
                result.AppendLine( "Signature    : member (null)" );
            }
            else
            {
                result.AppendLine( "Signature    : " + ( Signature.Sig == null ? "(null)" :
                    " [" + Signature.Sig.Length + "] " + Signature.ToString() ) );
            }

            return result.ToString();
        }
    }
}
