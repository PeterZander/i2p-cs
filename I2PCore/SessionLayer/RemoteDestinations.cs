using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Collections.Concurrent;

namespace I2PCore.SessionLayer
{
    public class RemoteDestinations
    {
        ConcurrentDictionary<I2PDestination, I2PLeaseSet> Subscribers =
                new ConcurrentDictionary<I2PDestination, I2PLeaseSet>();

        public TickCounter LastUpdate { get; private set; } = new TickCounter();

        public void LeaseSetReceived( I2PLeaseSet ls )
        {
            Logging.LogDebug(
                $"RemoteDestinations: updating {ls.Destination}" );

            Subscribers[ls.Destination] = ls;
        }

        public void PurgeExpired()
        {
            foreach( var dest in Subscribers )
            {
                dest.Value.RemoveExpired();
            }

            var remove = Subscribers.Where( d => !d.Value.Leases.Any() );

            foreach ( var dest in remove )
            {
                Logging.LogDebug(
                    $"RemoteDestinations: Removing {dest}" );

                Subscribers.TryRemove( dest.Key, out _ );
            }
        }

        public IEnumerable<KeyValuePair<I2PDestination, I2PLeaseSet>> DestinationsToUpdate
        {
            get
            {
                PurgeExpired();
                LastUpdate.SetNow();
                return Subscribers;
            }
        }

        public bool IsEmpty
        {
            get
            {
                PurgeExpired();
                return Subscribers.IsEmpty;
            }
        }

        public I2PLeaseSet GetLeases( I2PDestination d )
        {
            return Subscribers.TryGetValue( d, out var ls ) ? ls : null;
        }
    }
}
