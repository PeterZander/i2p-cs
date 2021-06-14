using System.Linq;
using I2PCore.Data;
using I2PCore.Utils;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace I2PCore.SessionLayer
{
    /// <summary>
    /// Update info of remote destinations when my leases change.
    /// </summary>
    public class RemoteDestinationsLeasesUpdates
    {
        public static TickSpan TimeBetweenLeasesUpdates = TickSpan.Minutes( 3 );

        public static int NumberOfResendsPerUpdate = 3;

        public class DestLeaseInfo
        {
            public int LookupFailures = 0;
            public ILeaseSet LeaseSet;
            
            /// <summary>
            /// Last time we used the cached remote LeaseSet
            /// </summary>
            internal TickCounter LastLeaseSetCacheUse = TickCounter.Now;

            /// <summary>
            /// Last time we updated the remote destination with our LeaseSet
            /// </summary>
            internal TickCounter LastRemoteUpdate = TickCounter.Now - TimeBetweenLeasesUpdates * 10;

            internal int NumberOfUpdatesSent = 0;
        }

        ConcurrentDictionary<I2PIdentHash, DestLeaseInfo> Subscribers =
                new ConcurrentDictionary<I2PIdentHash, DestLeaseInfo>();
        private readonly ClientDestination Owner;

        public RemoteDestinationsLeasesUpdates( ClientDestination owner )
        {
            Owner = owner;
        }

        /// <summary>
        /// This is used for destinations we have actively looked up.
        /// </summary>
        /// <param name="ls">Ls.</param>
        public void LeaseSetReceived( ILeaseSet ls )
        {
            if ( ls.Destination.IdentHash == Owner.Destination.IdentHash )
            {
                // that is me
#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug(
                    $"{Owner}: RemoteDestinationsLeasesUpdates: " +
                    $"LeaseSetReceived: discarding my lease set." );
#endif
                return;
            }

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

                if ( ls.Expire > info?.LeaseSet?.Expire )
                {
                    info.LeaseSet = ls;
                }
            }
        }

        /// <summary>
        /// This is for any received lease update
        /// </summary>
        /// <param name="ls">Ls.</param>
        internal void PassiveLeaseSetUpdate( ILeaseSet ls )
        {
            if ( Subscribers.TryGetValue( ls.Destination.IdentHash, out var info ) )
            {
                Logging.LogDebugData(
                    $"{Owner} RemoteDestinations: updating LeaseSet for {ls.Destination} (passive)" );

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
                    .Where( s => s.Value.LastRemoteUpdate.DeltaToNow > TimeBetweenLeasesUpdates )
                    .OrderByDescending( s => s.Value.LastRemoteUpdate.DeltaToNow )
                    .Take( maxcount )
                    .Select( s => { s.Value.LastRemoteUpdate.SetNow(); return s; } )
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

            if ( result?.LeaseSet is null )
            {
                var cachedls = NetDb.Inst.FindLeaseSet( d );
                if ( cachedls != null )
                {
                    LeaseSetReceived( cachedls );
                    return Subscribers.TryGetValue( d, out var lsr ) ? lsr : null;
                }
            }

            result?.LastLeaseSetCacheUse?.SetNow();
            return result;
        }

        public bool NeedsLeasesUpdate( I2PIdentHash d, bool updateactivity = true )
        {
            if ( !Subscribers.TryGetValue( d, out var remote ) )
            {
                Logging.LogDebug(
                    $"{Owner} RemoteDestinations: NeedsLeasesUpdate: adding {d.Id32Short}" );

                remote = new DestLeaseInfo();
                Subscribers[d] = remote;
            }

            var result = remote.LastRemoteUpdate.DeltaToNow > TimeBetweenLeasesUpdates;
            if ( result && updateactivity )
            {
                if ( ++remote.NumberOfUpdatesSent >= NumberOfResendsPerUpdate )
                {
                    Logging.LogDebug(
                        $"{Owner}: NeedsLeasesUpdate: " +
                        $"Sending final update ({NumberOfResendsPerUpdate}) to {d.Id32Short}" );

                    remote.LastRemoteUpdate.SetNow();
                    remote.NumberOfUpdatesSent = 0;
                }
                else
                {
                    Logging.LogDebug(
                        $"{Owner}: NeedsLeasesUpdate: " +
                        $"Sending our LeaseSet update ({remote.NumberOfUpdatesSent}) to {d.Id32Short}" );
                }
            }

            return result;
        }

        internal void LeaseSetIsUpdated()
        {
            var t = TickCounter.Now - TimeBetweenLeasesUpdates * 10;
            foreach ( var s in Subscribers )
            {
                s.Value.LastRemoteUpdate = t;
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
