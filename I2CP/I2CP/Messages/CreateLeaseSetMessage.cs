using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using Org.BouncyCastle.Math;

namespace I2P.I2CP.Messages
{
    public class CreateLeaseSetMessage: I2CPMessage
    {
        public ushort SessionId;
        public I2PLeaseInfo Info;
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

        public override void Write( List<byte> dest )
        {
            dest.AddRange( BitConverter.GetBytes( SessionId ) );
            var dummy = new I2PSigningPrivateKey( new I2PCertificate() );
            dummy.Write( dest );
            //Info.PrivateSigningKey.Write( dest );
            Info.PrivateKey.Write( dest );
            Leases.Write( dest );

            /*
            var ar = dest.ToArray();
            int ix = 22;
            var pivk = new I2PPrivateKey( ar, ref ix );
            ix = 665;
            var refpubk = new I2PPublicKey( ar, ref ix );
            var diff = ( new I2PPublicKey( pivk ) ).Key.Subtract( refpubk.Key );
            var ok = diff.CompareTo( BigInteger.Zero ) == 0;
             */
        }
    }
}
