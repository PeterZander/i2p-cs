using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.TransportLayer.SSU
{
    public class RelayIntro
    {
        public readonly BufLen AliceIpSize;
        public readonly BufLen AliceIpNumber;
        public readonly BufLen AlicePort;
        public readonly BufLen ChallengeSize;
        public readonly BufLen Challenge;

        public IPEndPoint AliceEndpoint { get { return new IPEndPoint( new IPAddress( AliceIpNumber.ToByteArray() ), AlicePort.PeekFlip16( 0 ) ); } }

        public RelayIntro( BufRef reader )
        {
            AliceIpSize = reader.ReadBufLen( 1 );
            AliceIpNumber = reader.ReadBufLen( AliceIpSize.Peek8( 0 ) );
            AlicePort = reader.ReadBufLen( 2 );
            ChallengeSize = reader.ReadBufLen( 1 );
            Challenge = reader.ReadBufLen( ChallengeSize.Peek8( 0 ) );
        }

        public override string ToString()
        {
            return string.Format( "RelayIntro: Alice IP#: {0}, Port: {1}",
                new IPAddress( AliceIpNumber.ToByteArray() ), AlicePort.PeekFlip16( 0 ) );
        }
    }
}
