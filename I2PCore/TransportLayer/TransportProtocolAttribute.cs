using System;
namespace I2PCore.TransportLayer
{
    /// <summary>
    /// Add to classes that implement ITransportProtocol and should be instanced
    /// to accept incomming and outgoing connections.
    /// The class decorated with this attribute must have a default constructor
    /// with no aruments.
    /// </summary>
    [AttributeUsage( AttributeTargets.Class, AllowMultiple = false )]
    public class TransportProtocolAttribute: Attribute
    {
    }
}
