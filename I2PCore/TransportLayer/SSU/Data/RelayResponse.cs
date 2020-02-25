using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.TransportLayer.SSU
{
    public class RelayResponse
    {
        public readonly BufLen CharlieIpSize;
        public readonly BufLen CharlieIpNumber;
        public readonly BufLen CharliePort;
        public readonly BufLen AliceIpSize;
        public readonly BufLen AliceIpNumber;
        public readonly BufLen AlicePort;
        public readonly BufLen Nonce;

        public IPEndPoint CharlieEndpoint { get { return new IPEndPoint( new IPAddress( CharlieIpNumber.ToByteArray() ), CharliePort.PeekFlip16( 0 ) ); } }

        public RelayResponse( BufRef reader )
        {
            CharlieIpSize = reader.ReadBufLen( 1 );
            CharlieIpNumber = reader.ReadBufLen( CharlieIpSize.Peek8( 0 ) );
            CharliePort = reader.ReadBufLen( 2 );
            AliceIpSize = reader.ReadBufLen( 1 );
            AliceIpNumber = reader.ReadBufLen( AliceIpSize.Peek8( 0 ) );
            AlicePort = reader.ReadBufLen( 2 );
            Nonce = reader.ReadBufLen( 4 );
        }

        public override string ToString()
        {
            return string.Format( "RelayResponse: Charlie IP#: {0}, Port: {1}, Alice IP#: {2}, Port: {3}, Nonce: {4}",
                new IPAddress( CharlieIpNumber.ToByteArray() ), CharliePort.PeekFlip16( 0 ),
                new IPAddress( AliceIpNumber.ToByteArray() ), AlicePort.PeekFlip16( 0 ),
                Nonce.PeekFlip32( 0 ) );
        }
    }
}
