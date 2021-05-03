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
        /// Protocol initial handshake is finished, and data can be sent.
        /// </summary>
        event Action<ITransport,I2PIdentHash> ConnectionEstablished;

        event Action<ITransport, II2NPHeader> DataBlockReceived;

        void Connect();

        void Send( I2NPMessage msg );

        void Terminate();
        bool IsTerminated { get; }

        /// <summary>
        /// Called by TransportProvider if a DatabaseStoreMessage for this transport was received.
        /// </summary>
        void DatabaseStoreMessageReceived( DatabaseStoreMessage dsm );

        I2PKeysAndCert RemoteRouterIdentity { get; }
        IPAddress RemoteAddress { get; }

        long BytesSent { get; }
        long BytesReceived { get; }

        /// <summary>
        /// Instance unique identifier for debugging.<!--
        /// </summary>
        string DebugId { get; }

        /// <summary>
        /// Abbriviated unique name of the protocol implemented.
        /// </summary>
        string Protocol { get; }
        
        bool IsOutgoing { get; }
    }
}
