using System;
using I2PCore.Data;

namespace I2PCore.TransportLayer
{
    public enum ProtocolCapabilities
    {
        /// <summary>
        /// Unable to communicate with host
        /// </summary>
        None = 0,

        /// <summary>
        /// Can only create outgoing connections to host
        /// </summary>
        Outgoing = 40,

        /// <summary>
        /// Can create outgoing and incoming connections to host
        /// </summary>
        Incoming = 60,

        /// <summary>
        /// Can use NAT traversal to connect to host
        /// </summary>
        NATTraversal = 80,

        /// <summary>
        /// Can maintain and host virtual networks like NAT taversal including introduction
        /// </summary>
        VirtualNetworkCreation = 100,
    }

    public interface ITransportProtocol
    {
        /// <summary>
        /// Occurs when connection created for an incomming connection.
        /// </summary>
        event Action<ITransport> ConnectionCreated;

        /// <summary>
        /// Returns what features the transport supports for the specific router based on the router info.
        /// </summary>
        /// <returns>The capability.</returns>
        /// <param name="router">Router.</param>
        ProtocolCapabilities ContactCapability( I2PRouterInfo router );

        /// <summary>
        /// Creates an outgoing session to the router and returns a reference to the session.
        /// </summary>
        /// <returns>The session.</returns>
        /// <param name="router">Router.</param>
        ITransport AddSession( I2PRouterInfo router );

        /// <summary>
        /// Return the number of currently blocked remote host addresses.
        /// </summary>
        /// <value>The blocked remote addresses count.</value>
        int BlockedRemoteAddressesCount { get; }
    }
}
