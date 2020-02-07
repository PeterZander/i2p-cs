using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    internal class PeerTestNonceInfo
    {
        public TickCounter Created = new TickCounter();
        public PeerTestRole Role;

        public PeerTestNonceInfo( PeerTestRole role )
        {
            Role = role;
        }
    }

    public partial class SSUHost
    {
        internal PeerTestState PeerTestInstance = new PeerTestState();
        Dictionary<uint, PeerTestNonceInfo> KnownPeerTestNonces = new Dictionary<uint, PeerTestNonceInfo>();

        internal PeerTestNonceInfo GetNonceInfo( uint nonce )
        {
            PeerTestNonceInfo nonceinfo;

            lock ( KnownPeerTestNonces )
            {
                var remove = KnownPeerTestNonces.Where( p => p.Value.Created.DeltaToNowMilliseconds > PeerTestState.PeerTestNonceLifetimeMilliseconds ).
                    Select( p => p.Key ).ToArray();
                foreach ( var key in remove ) KnownPeerTestNonces.Remove( key );

                if ( !KnownPeerTestNonces.TryGetValue( nonce, out nonceinfo ) ) nonceinfo = null;
            }

            return nonceinfo;
        }

        internal void SetNonceInfo( uint nonce, PeerTestRole role )
        {
            lock ( KnownPeerTestNonces )
            {
                KnownPeerTestNonces[nonce] = new PeerTestNonceInfo( role );
            }
        }

        internal void SendFirstPeerTestToCharlie( PeerTest msg )
        {
            lock ( Sessions )
            {
                if ( Sessions.Count > 0 ) Sessions.Random().SendFirstPeerTestToCharlie( msg );
            }
        }
    }
}
