using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.TransportLayer.SSU
{
    public class SessionCreated
    {
        I2PCertificate Certificate;

        public readonly BufLen Y;
        public readonly BufLen Address;
        public readonly BufLen Port;
        public readonly BufLen RelayTag;
        public readonly BufLen SignOnTime;
        public readonly BufLen Signature;
        public readonly BufLen SignatureEncrBuf;

        public DateTime Signon { get { return SSUHost.SSUDateTime( SignOnTime.PeekFlip32( 0 ) ); } }

        public SessionCreated( BufRefLen reader, I2PCertificate cert )
        {
            Certificate = cert;

            Y = reader.ReadBufLen( Certificate.PublicKeyLength );
            var ipsize = reader.Read8();
            Address = reader.ReadBufLen( ipsize );
            Port = reader.ReadBufLen( 2 );
            RelayTag = reader.ReadBufLen( 4 );
            SignOnTime = reader.ReadBufLen( 4 );
            SignatureEncrBuf = new BufLen( reader, 0, cert.SignatureLength + BufUtils.Get16BytePadding( cert.SignatureLength ) );
            Signature = reader.ReadBufLen( cert.SignatureLength );
        }
    }
}
