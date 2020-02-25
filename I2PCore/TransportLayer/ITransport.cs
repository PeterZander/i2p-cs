using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.SessionLayer;
using System.Net;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TransportLayer
{
    public interface ITransport
    {
        event Action<ITransport, Exception> ConnectionException;
        event Action<ITransport> ConnectionShutDown;

        /// <summary>
        /// Diffie-Hellman negotiations completed.
        /// </summary>
        event Action<ITransport,I2PIdentHash> ConnectionEstablished;

        event Action<ITransport, II2NPHeader> DataBlockReceived;

        void Connect();

        void Send( I2NPMessage msg );

        void Terminate();
        bool Terminated { get; }

        I2PKeysAndCert RemoteRouterIdentity { get; }
        IPAddress RemoteAddress { get; }

        long BytesSent { get; }
        long BytesReceived { get; }

        string DebugId { get; }
        string Protocol { get; }
        bool Outgoing { get; }
    }
}
