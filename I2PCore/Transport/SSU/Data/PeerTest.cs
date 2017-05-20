using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;

namespace I2PCore.Transport.SSU
{
    public class PeerTest
    {
        public readonly BufLen TestNonce;
        public readonly BufLen AliceIPAddrSize;
        public readonly BufLen AliceIPAddr;
        public readonly BufLen AlicePort;
        public readonly BufLen IntroKey;

        public PeerTest( BufRef reader )
        {
            TestNonce = reader.ReadBufLen( 4 );
            AliceIPAddrSize = reader.ReadBufLen( 1 );
            AliceIPAddr = reader.ReadBufLen( AliceIPAddrSize[0] );
            AlicePort = reader.ReadBufLen( 2 );
            IntroKey = reader.ReadBufLen( 32 );
        }

        public PeerTest( BufLen nonce, IPAddress aliceip, int aliceport, BufLen introkey )
        {
            TestNonce = nonce;
            var ab = aliceip.GetAddressBytes();
            AliceIPAddrSize = (BufLen)(byte)ab.Length;
            AliceIPAddr = new BufLen( ab );
            AlicePort = BufUtils.Flip16BL( (ushort)aliceport );
            IntroKey = introkey;
        }

        public PeerTest( BufLen nonce, BufLen aliceip, BufLen aliceport, BufLen introkey )
        {
            TestNonce = nonce;
            AliceIPAddrSize = (BufLen)(byte)aliceip.Length;
            AliceIPAddr = aliceip;
            AlicePort = aliceport;
            IntroKey = introkey;
        }

        public void WriteTo( BufRef writer )
        {
            writer.Write( TestNonce );
            writer.Write( AliceIPAddrSize );
            writer.Write( AliceIPAddr );
            writer.Write( AlicePort );
            writer.Write( IntroKey );
        }

        public bool IPAddressOk { get { return AliceIPAddr.Length == 4 || AliceIPAddr.Length == 6; } }

        public override string ToString()
        {
            string addr;
            if ( IPAddressOk )
            {
                addr = ( new IPAddress( AliceIPAddr.ToByteArray() ) ).ToString();
            }
            else if ( AliceIPAddr.Length == 0 )
            {
                addr = "<empty>";
            }
            else
            {
                addr = AliceIPAddr.ToString();
            }
            return string.Format( "PeerTest: Test nonce: {0}, Alice IP#: {1}, Port: {2}, Intro key: {3}",
                TestNonce.PeekFlip32( 0 ), addr, AlicePort.PeekFlip16( 0 ),
                FreenetBase64.Encode( IntroKey ) );
        }
    }
}
