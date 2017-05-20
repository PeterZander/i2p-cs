using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;

namespace I2PCore.Transport.SSU
{
    /*
            Alice                  Bob                  Charlie
    PeerTest ------------------->
                             PeerTest-------------------->
                                <-------------------PeerTest
         <-------------------PeerTest
         <------------------------------------------PeerTest
    PeerTest------------------------------------------>
         <------------------------------------------PeerTest
     
     * https://geti2p.net/en/docs/transport/ssu
     */

    internal enum PeerTestRole { Alice, Bob, Charlie }

    internal class PeerTestState
    {
        internal const int PeerTestNonceLifetimeMilliseconds = 20000; 
        
        SSUHost Host;
        I2PRouterAddress Addr;
        I2PKeysAndCert Dest;

        SSUSession Session;

        enum TestState { Start, GotReplyFromBob, GotReplyFromBobCharlie, GotReplyFromCharlie };
        TestState State = TestState.Start;

        internal int BobPeerTestsSent = 0;
        internal int CharliePeerTestsSent = 0;

        internal PeerTestState()
        {
        }

        private PeerTestState( SSUHost host, I2PRouterAddress addr, I2PKeysAndCert dest )
        {
            Host = host;
            Addr = addr;
            Dest = dest;

            Session = (SSUSession)Host.AddSession( addr, dest );
            //Session.StartPeerTest( this );
        }

        internal bool OurTestNonce( I2PCore.Utils.BufLen bufLen )
        {
            return false;
        }

        internal void CharlieDirectResponseReceived( PeerTest msg )
        {
        }
    }
}
