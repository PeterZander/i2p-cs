using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net;
using Org.BouncyCastle.Math;

namespace I2PCore.TransportLayer.SSU
{
    public class SessionRequest
    {
        I2PCertificate Certificate;

        public readonly BufLen X;
        public BigInteger XKey { get { return new BigInteger( 1, X.BaseArray, X.BaseArrayOffset, X.Length ); } }

        public readonly BufLen Address;

        public SessionRequest( BufRef reader, I2PCertificate cert )
        {
            Certificate = cert;

            X = reader.ReadBufLen( Certificate.PublicKeyLength );
            var ipsize = reader.Read8();
            Address = reader.ReadBufLen( ipsize );
        }
    }
}
