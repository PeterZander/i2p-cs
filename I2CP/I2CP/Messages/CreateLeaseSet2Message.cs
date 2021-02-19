using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Math;

namespace I2P.I2CP.Messages
{
    public class CreateLeaseSet2Message: I2CPMessage
    {
        public ushort SessionId;
        public I2PSigningPrivateKey DSAPrivateSigningKey;
        public IList<I2PPrivateKey> PrivateKeys;
        public I2PLeaseSet Leases;
        public I2PLeaseSet2 Leases2;

        public CreateLeaseSet2Message( BufRef reader, I2CPSession session ) 
                : base( ProtocolMessageType.CreateLeaseSet2Message )
        {
            SessionId = reader.ReadFlip16();

            var lstype = reader.Read8();
            switch( lstype )
            {
                case 1: // LS
                    Leases = new I2PLeaseSet( reader );
                    break;

                case 3: // LS2
                    Leases2 = new I2PLeaseSet2( reader );
                    break;

                case 5: // Enc LS2
                    throw new NotImplementedException();

                case 7: // Meta LS2
                    throw new NotImplementedException();
            }

            PrivateKeys = new List<I2PPrivateKey>();
            var privkeycount = reader.Read8();
            for( int i = 0; i < privkeycount; ++i )
            {
                var etype = (I2PPublicKey.KeyTypes)reader.ReadFlip16();
                var keylen = reader.ReadFlip16();
                PrivateKeys.Add( new I2PPrivateKey( reader, new I2PCertificate( etype, keylen ) ) );
            }
        }

        public override void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
