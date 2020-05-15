using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;

namespace I2PCore.TransportLayer.SSU
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
        I2PRouterInfo Router;

        SSUSession Session;

        enum TestState { Start, GotReplyFromBob, GotReplyFromBobCharlie, GotReplyFromCharlie };
        TestState State = TestState.Start;

        internal int BobPeerTestsSent = 0;
        internal int CharliePeerTestsSent = 0;

        internal PeerTestState()
        {
        }

        private PeerTestState( SSUHost host, I2PRouterInfo router )
        {
            Host = host;
            Router = router;

            Session = (SSUSession)Host.AddSession( router );
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
