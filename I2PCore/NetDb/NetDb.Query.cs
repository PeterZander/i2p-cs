using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.SessionLayer;
using System.Collections.Concurrent;

namespace I2PCore
{
    public partial class NetDb
    {
        public int RouterCount { get => RouterInfos.Count; }
        public int FloodfillCount { get => FloodfillInfos.Count; }

        private I2PIdentHash GetRandomRouter(
            RouletteSelection<I2PRouterInfo, I2PIdentHash> r,
            ICollection<I2PIdentHash> exclude,
            bool exploratory )
        {
            I2PIdentHash result;
            var me = RouterContext.Inst.MyRouterIdentity.IdentHash;

            var retries = 0;

            if ( exploratory )
            {
                var subset = exclude is null
                            ? RouterInfos
                            : RouterInfos.Where( k => !exclude.Contains( k.Key ) );
                do
                {
                    result = subset

                        .Random()
                        .Key;
                } while ( result == me && ++retries < 20 );

                return result;
            }

            bool tryagain;
            do
            {
                result = r.GetWeightedRandom( r.Wheel.Count() < 300 ? null : exclude );
                tryagain = result == me;
            } while ( tryagain && ++retries < 20 );

            //Logging.LogInformation( $"GetRandomRouter selected {result}: {exclude.Any( k2 => k2 == result )}" );

            return result;
        }

        I2PRouterInfo GetRandomRouterInfo( RouletteSelection<I2PRouterInfo, I2PIdentHash> r, bool exploratory )
        {
            return this[GetRandomRouter( r, null, exploratory )];
        }

        public I2PRouterInfo GetRandomRouterInfo( bool exploratory )
        {
            return GetRandomRouterInfo( Roulette, exploratory );
        }

#if LOG_ROUTER_SELECTION_HISTORY && DEBUG
        ConcurrentDictionary<I2PIdentHash, int> RouterSelectionHistory =
            new ConcurrentDictionary<I2PIdentHash, int>();

        PeriodicAction LogRouterSelectionHistory = new PeriodicAction( TickSpan.Minutes( 5 ) );
#endif        

        public I2PIdentHash GetRandomRouterForTunnelBuild( bool exploratory )
        {
            I2PIdentHash result;

            result = GetRandomRouter( Roulette, null, exploratory );
            if ( result is null ) return null;

#if LOG_ROUTER_SELECTION_HISTORY && DEBUG
            if ( !exploratory )
            {
                RouterSelectionHistory.AddOrUpdate( result, ih => 1, ( ih, count ) => count + 1 );
            }

            LogRouterSelectionHistory.Do( () =>
            {
                var sum = RouterSelectionHistory.Sum( rh => rh.Value );
                var hist = RouterSelectionHistory
                    .OrderByDescending( h => h.Value )
                    .ToArray();

                Logging.LogDebug( $"NetDb: GetRandomRouterForTunnelBuild: Different routers: " +
                    $"{hist.Count()} / {Roulette.Count} ({(100.0*hist.Count())/Roulette.Count:F2}%)" );
                foreach( var one in hist )
                {
                    Logging.LogDebug( $"NetDb: GetRandomRouterForTunnelBuild: {one.Key.Id32Short} {one.Value,8} " +
                        $"{(1E6*one.Value)/sum,10:F0} ppm, all {(100.0*one.Value)/Roulette.Count,8:F3} %" );
                }
            } );
#endif        

            return result;
        }

        public IEnumerable<I2PIdentHash> GetRandomRoutersForTunnelBuild( bool exploratory, int hops )
        {
            if ( hops <= 0 ) throw new ArgumentException( "Hops must be > 0" );

            var exclude = new HashSet<I2PIdentHash>();

            for ( int i = 0; i < hops; ++i )
            {
                var retry = 0;
                I2PIdentHash ih;

                do
                {
                    ih = NetDb.Inst.GetRandomRouterForTunnelBuild( exploratory );
                } while ( exclude.Contains( ih ) && ++retry < 5 );

                exclude.Add( ih );
                yield return ih;
            }
        }

        public I2PRouterInfo GetRandomNonFloodfillRouterInfo( bool exploratory )
        {
            return GetRandomRouterInfo( RouletteNonFloodFill, exploratory );
        }

        public I2PRouterInfo GetRandomFloodfillRouterInfo( bool exploratory )
        {
            return GetRandomRouterInfo( RouletteFloodFill, exploratory );
        }

        readonly ItemFilterWindow<I2PIdentHash> RecentlyUsedForFF = new ItemFilterWindow<I2PIdentHash>( TickSpan.Minutes( 15 ), 2 );

        public I2PIdentHash GetRandomFloodfillRouter( bool exploratory )
        {
            return GetRandomRouter( RouletteFloodFill, RecentlyUsedForFF.ToHashSet(), exploratory );
        }

        public IEnumerable<I2PIdentHash> GetRandomFloodfillRouter( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i )
            {
                yield return GetRandomFloodfillRouter( exploratory );
            }
        }

        public IEnumerable<I2PRouterInfo> GetRandomFloodfillRouterInfo( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i )
            {
                yield return GetRandomFloodfillRouterInfo( exploratory );
            }
        }

        public IEnumerable<I2PRouterInfo> GetRandomNonFloodfillRouterInfo( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i )
            {
                yield return GetRandomNonFloodfillRouterInfo( exploratory );
            }
        }

        public IEnumerable<I2PIdentHash> GetClosestFloodfill(
                I2PIdentHash dest,
                int count,
                ICollection<I2PIdentHash> exclude )
        {
            var subset = ( exclude != null && exclude.Any() )
                ? FloodfillInfos.Where( inf => !exclude.Contains( inf.Key ) )
                : FloodfillInfos;

            var refkey = dest.RoutingKey;

            return subset
                .Select( ri => new
                {
                    Id = ri.Key,
                    Dist = ri.Key ^ refkey,
                    Q = ri.Value.CachedStatistics,
                } )
                .OrderBy( p => p.Dist )
                .Take( count )
                .Select( p => p.Id )
                .ToArray();
        }

        public IEnumerable<I2PRouterInfo> GetClosestFloodfillInfo(
            I2PIdentHash reference,
            int count,
            ICollection<I2PIdentHash> exclude )
        {
            return Find( GetClosestFloodfill( reference, count, exclude ) );
        }
    }
}
