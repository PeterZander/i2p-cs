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
    /// <summary>
    /// Update info of remote destinations when my leases change.
    /// </summary>
    public class RemoteDestinationsLeasesUpdates
    {
        public TickSpan TimeBetweenLeasesUpdates = TickSpan.Minutes( 3 );

        public class DestLeaseInfo
        {
            public int LookupFailures = 0;
            public I2PLeaseSet LeaseSet;
            internal TickCounter LastUse = TickCounter.Now;
            internal TickCounter LastUpdate = new TickCounter();
        }

        ConcurrentDictionary<I2PIdentHash, DestLeaseInfo> Subscribers =
                new ConcurrentDictionary<I2PIdentHash, DestLeaseInfo>();
        private readonly object Owner;

        public RemoteDestinationsLeasesUpdates( object owner )
        {
            Owner = owner;
        }

        /// <summary>
        /// This is used for destinations we have actively looked up.
        /// </summary>
        /// <param name="ls">Ls.</param>
        public void LeaseSetReceived( I2PLeaseSet ls )
        {
            if ( !Subscribers.TryGetValue( ls.Destination.IdentHash, out var info ) )
            {
                Logging.LogDebug(
                    $"{Owner} RemoteDestinations: adding {ls.Destination}" );

                info = new DestLeaseInfo { LeaseSet = ls };
                Subscribers[ls.Destination.IdentHash] = info;
            }
            else
            {
                Logging.LogDebug(
                    $"{Owner} RemoteDestinations: updating {ls.Destination}" );

                info.LeaseSet = ls;
            }
        }

        /// <summary>
        /// This is for any received lease update
        /// </summary>
        /// <param name="ls">Ls.</param>
        internal void PassiveLeaseSetUpdate( I2PLeaseSet ls )
        {
            if ( Subscribers.TryGetValue( ls.Destination.IdentHash, out var info ) )
            {
                Logging.LogDebug(
                    $"{Owner} RemoteDestinations: updating {ls.Destination} (passive)" );

                info.LeaseSet = ls;
            }
        }

        public void PurgeExpired()
        {
            foreach( var dest in Subscribers )
            {
                dest.Value.LeaseSet.RemoveExpired();
            }
        }

        public IEnumerable<KeyValuePair<I2PIdentHash, DestLeaseInfo>> DestinationsToUpdate( int maxcount )
        {
            PurgeExpired();
            var result = Subscribers
                    .Where( s => s.Value.LastUpdate.DeltaToNow > TimeBetweenLeasesUpdates )
                    .OrderByDescending( s => s.Value.LastUpdate.DeltaToNow )
                    .Take( maxcount )
                    .Select( s => { s.Value.LastUpdate.SetNow(); return s; } )
                    .ToArray();

            return result;
        }

        public bool IsEmpty
        {
            get
            {
                PurgeExpired();
                return Subscribers.IsEmpty;
            }
        }

        public DestLeaseInfo GetLeases( I2PIdentHash d, bool updateactivity = true )
        {
            var result = Subscribers.TryGetValue( d, out var ls ) ? ls : null;

            if ( result != null && updateactivity )
            {
                result.LastUse.SetNow();
                result.LastUpdate.SetNow();
            }

            return result;
        }

        public bool NeedsLeasesUpdate( I2PIdentHash d, bool updateactivity = true )
        {
            var remote = Subscribers.TryGetValue( d, out var ls ) ? ls : null;
            if ( remote is null ) return false;

            var result = remote.LastUpdate.DeltaToNow > TimeBetweenLeasesUpdates;
            if ( result && updateactivity )
            {
                remote.LastUse.SetNow();
                remote.LastUpdate.SetNow();
            }

            return result;
        }

        internal void LeaseSetIsUpdated()
        {
            var t = TickCounter.Now - TickSpan.Minutes( 10 );
            foreach ( var s in Subscribers )
            {
                s.Value.LastUpdate = t;
            }
        }

        internal void Remove( I2PIdentHash d )
        {
            Subscribers.TryRemove( d, out var _ );
        }

        internal int LookupFailures( I2PIdentHash hash )
        {
            var info = Subscribers.FirstOrDefault( s => s.Key == hash );
            var isdefault = Equals( info, default( KeyValuePair<I2PIdentHash, DestLeaseInfo> ) );
            return isdefault ? int.MaxValue : ++info.Value.LookupFailures;
        }
    }
}
