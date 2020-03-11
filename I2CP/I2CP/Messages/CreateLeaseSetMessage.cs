using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Math;

namespace I2P.I2CP.Messages
{
    public class CreateLeaseSetMessage: I2CPMessage
    {
        public ushort SessionId;
        public I2PLeaseInfo Info;
        public I2PSigningPrivateKey DSAPrivateSigningKey;
        public I2PPrivateKey PrivateKey;
        public I2PLeaseSet Leases;

        public CreateLeaseSetMessage( 
            I2PDestination dest,
            ushort sessionid, 
            I2PLeaseInfo info,
            List<I2PLease> leases ): base( ProtocolMessageType.CreateLS )
        {
            SessionId = sessionid;
            Info = info;
            Leases = new I2PLeaseSet( dest, leases, info );
        }

        public CreateLeaseSetMessage( BufRef reader, I2CPSession session ) 
                : base( ProtocolMessageType.CreateLS )
        {
            SessionId = reader.ReadFlip16();

            var cert = session.SessionIds[SessionId].Config.Destination.Certificate;

            DSAPrivateSigningKey = new I2PSigningPrivateKey( 
                    reader, 
                    new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ) );

            PrivateKey = new I2PPrivateKey( reader, cert );
            Leases = new I2PLeaseSet( reader );
        }

        static readonly byte[] TwentyBytes = { 0, 0, 0, 0, 0, 0, 0, 0, 
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        public override void Write( BufRefStream dest )
        {
            dest.Write( (BufRefLen)BufUtils.Flip16BL( SessionId ) );
            dest.Write( TwentyBytes );
            PrivateKey.Write( dest );
            Leases.Write( dest );
        }
    }
}
