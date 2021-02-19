using System;
using System.Collections.Generic;
using I2PCore.TunnelLayer.I2NP.Messages;

namespace I2PCore.Data
{
    public interface ILeaseSet
    {
        DatabaseStoreMessage.MessageContent MessageType { get; }
        I2PDestination Destination { get; }
        IEnumerable<ILease> Leases { get; }
        DateTime Expire { get; }
        IEnumerable<I2PPublicKey> PublicKeys { get; }
        
        void RemoveExpired();

        byte[] ToByteArray();
    }
}