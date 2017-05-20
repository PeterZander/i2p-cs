using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Tunnel.I2NP;

namespace I2PCore.Tunnel
{
    // Manages "ElGamal/AES+SessionTags" or "2048bit ElGamal, AES256, SHA256, and 32 byte nonces."
    // sessions from one starting point (Local Destination or Router) to multiple Destinations (or Routers).
    public class DestinationSessions
    {
        const int ReceivedTagLifetimeMinutes = 16;
        const int SentTagLifetimeMinutes = 12;

        Dictionary<I2PIdentHash, DestinationSession> Sessions = new Dictionary<I2PIdentHash, DestinationSession>();
        OutboundTunnelSelector OutTunnelSelector;
        InboundTunnelSelector InTunnelSelector;

        public DestinationSessions( OutboundTunnelSelector outtunnelsel, InboundTunnelSelector intunnelsel )
        {
            OutTunnelSelector = outtunnelsel;
            InTunnelSelector = intunnelsel;
        }

        public DestinationSession this[I2PKeysAndCert dest]
        {
            get
            {
                lock ( Sessions )
                {
                    DestinationSession result;
                    if ( !Sessions.TryGetValue( dest.IdentHash, out result ) )
                    {
                        result = new DestinationSession( dest, OutTunnelSelector, InTunnelSelector );
                        Sessions[dest.IdentHash] = result;
                    }
                    return result;
                }
            }
        }

        /*
        public GarlicCreationInfo GenerateGarlicMessage( I2PKeysAndCert dest, Garlic msg )
        {
            DestinationSession session;

            lock ( Sessions )
            {
                if ( !Sessions.TryGetValue( dest, out session ) )
                {
                    session = new DestinationSession( dest, OutTunnelSelector, InTunnelSelector );
                    Sessions[dest] = session;
                }
            }

            return session.Encrypt( msg, I2NPHeader.GenerateMessageId() );
        }

        public GarlicCreationInfo CreateMessage( I2PKeysAndCert dest, params GarlicCloveDelivery[] messages )
        {
            return GenerateGarlicMessage( dest, Garlic.Create( messages ) );
        }

        public GarlicCreationInfo CreateMessage( I2PKeysAndCert dest, I2PDate expiration, params GarlicCloveDelivery[] messages )
        {
            return GenerateGarlicMessage( dest, Garlic.Create( expiration, messages ) );
        }
         */

        internal GarlicCreationInfo Send( I2PKeysAndCert dest, bool explack, params GarlicCloveDelivery[] messages )
        {
            DestinationSession session = this[dest];
            return session.Send( explack, messages );
        }

        internal void Run()
        {
            DestinationSession[] sessions;

            lock ( Sessions )
            {
                sessions = Sessions.Select( p => p.Value ).ToArray();
            }

            foreach ( var sess in sessions ) sess.Run();
        }

        internal void LocalLeaseSetUpdated( I2PLeaseSet leaseset )
        {
            this[leaseset.Destination].LocalLeaseSetUpdated( leaseset );
        }

        internal void RemoteLeaseSetUpdated( I2PLeaseSet leaseset )
        {
            this[leaseset.Destination].RemoteLeaseSetUpdated( leaseset );
        }

        internal bool RemoteLeaseSetKnown( I2PIdentHash hash )
        {
            DestinationSession session;

            lock ( Sessions )
            {
                if ( Sessions.TryGetValue( hash, out session ) )
                {
                    return session.RemoteLeaseSetKnown();
                }
            }

            return false;
        }
    }
}
