using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.Threading;
using I2PCore.Utils;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace I2PCore
{
    public class RoutersStatistics
    {
        enum StoreRecordId : int { RouterStatistics = 1 };
        ConcurrentDictionary<I2PIdentHash, RouterStatistics> Routers = 
                new ConcurrentDictionary<I2PIdentHash, RouterStatistics>();

        public RouterStatistics this[ I2PIdentHash ix ]
        {
            get
            {
                if ( !Routers.TryGetValue( ix, out var stat ) )
                {
                    stat = new RouterStatistics( ix );
                    Routers[ix] = stat;
                }
                else
                {
                    stat = Routers[ix];
                }
                return stat;
            }
        }

        private static Store GetStore()
        {
            return BufUtils.GetStore( 
                        NetDb.Inst.GetFullPath( "statistics.sto" ), 
                        -1 );
        }

        public void Load()
        {
            using ( var s = GetStore() )
            {
                var readsw = new Stopwatch();
                var constrsw = new Stopwatch();
                var dicsw = new Stopwatch();
                var sw2 = new Stopwatch();
                sw2.Start();
                var ix = 0;
                while ( ( ix = s.Next( ix ) ) > 0 )
                {
                    readsw.Start();
                    var data = s.Read( ix );
                    readsw.Stop();

                    var reader = new BufRefLen( data );
                    switch ( (StoreRecordId)reader.Read32() )
                    {
                        case StoreRecordId.RouterStatistics:
                            constrsw.Start();
                            var one = new RouterStatistics( reader );
                            constrsw.Stop();

                            dicsw.Start();
                            Routers[one.Id] = one;
                            dicsw.Stop();

                            one.StoreIx = ix;
                            break;

                        default:
                            s.Delete( ix );
                            break;
                    }
                }
                sw2.Stop();
                Logging.Log( $"Statistics load: [{Routers.Count}] Total: {sw2.Elapsed}, " +
                    $"Read(): {readsw.Elapsed}, Constr: {constrsw.Elapsed}, " +
                    $"Dict: {dicsw.Elapsed} " );

                // var times = Destinations.Select( d => d.Value.TunnelBuildTimeMsPerHop.ToString() );
                // System.IO.File.WriteAllLines( "/tmp/ct.txt", times );
            }
        }

        const float twoweeks = 1000 * 60 * 60 * 24 * 14;

        public void Save()
        {
            var sw2 = new Stopwatch();
            sw2.Start();
            var deleted = 0;
            var updated = 0;
            var created = 0;
            using ( var s = GetStore() )
            {
                if ( !Routers.Any() ) return;

                foreach ( var one in Routers.ToArray() )
                {
                    if ( one.Value.Deleted && one.Value.StoreIx > 0 )
                    {
                        s.Delete( one.Value.StoreIx );
                        one.Value.StoreIx = -1;

                        Routers.TryRemove( one.Key, out _ );
                        ++deleted;
                        continue;
                    }

                    var rec = new BufLen[] 
                    { 
                        new BufLen( BitConverter.GetBytes( (int)StoreRecordId.RouterStatistics ) ),
                        new BufLen( one.Value.ToByteArray() ) 
                    };

                    if ( one.Value.StoreIx > 0 )
                    {
                        if ( one.Value.Updated )
                        {
                            s.Write( rec, one.Value.StoreIx );
                            ++updated;
                        }
                    }
                    else
                    {
                        one.Value.StoreIx = s.Write( rec );
                        ++created;
                    }

                    one.Value.Updated = false;
                }
            }
            sw2.Stop();
            Logging.Log( $"Statistics save: {sw2.Elapsed}, {created} created, " +
                $"{updated} updated, {deleted} deleted." );
        }

        public delegate void Accessor( RouterStatistics ds );

        public void Update( I2PIdentHash target, Accessor acc, bool success )
        {
            var rec = this[target];
            rec.Updated = true;
            if ( success ) rec.LastSeen = I2PDate.Now;
            acc( rec );
        }

        public void SuccessfulConnect( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.SuccessfulConnects ), true );
        }

        public void FailedToConnect( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FailedConnects ), false );
        }

        public void DestinationInformationFaulty( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.InformationFaulty ), false );
        }

        public void SlowHandshakeConnect( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.SlowHandshakeConnect ), false );
        }

        public void SuccessfulTunnelMember( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.SuccessfulTunnelMember ), true );
        }

        public void MaxBandwidth( I2PIdentHash hash, Bandwidth bw )
        {
            Update( hash, ds => ds.MaxBandwidthSeen = Math.Max( ds.MaxBandwidthSeen, bw.BitrateMax ), false );
        }

        public void DeclinedTunnelMember( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.DeclinedTunnelMember ), false );
        }

        public void SuccessfulTunnelTest( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.SuccessfulTunnelTest ), true );
        }

        public void FailedTunnelTest( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FailedTunnelTest ), false );
        }

        public void TunnelBuildTimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.TunnelBuildTimeout ), false );
        }

        public void TunnelBuildTimeMsPerHop( I2PIdentHash hash, long ms )
        {
            Update( hash, ds => ds.TunnelBuildTimeMsPerHop = ( long)( ( 9.0 * ds.TunnelBuildTimeMsPerHop + ms ) / 10.0 ), true );
        }

        public void FloodfillUpdateTimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FloodfillUpdateTimeout ), false );
        }

        public void FloodfillUpdateSuccess( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FloodfillUpdateSuccess ), true );
        }

        public void IsFirewalledUpdate( I2PIdentHash hash, bool isfw )
        {
            Update( hash, ds => ds.IsFirewalled = isfw, true );
        }

        internal static float BandwidthMax = 0f;

        public void UpdateScore()
        {
            var rar = Routers.ToArray();

            if ( !rar.Any() ) return;

            BandwidthMax = rar.Max( r => r.Value.MaxBandwidthSeen );
            foreach ( var one in rar ) one.Value.UpdateScore();
        }

        bool OffsetCompare( long fail, long offset, long success, long multip )
        {
            return fail > offset && fail > multip * success;
        }

        bool NodeInactive( RouterStatistics d )
        {
            if ( d.InformationFaulty > 0 ) 
            {
                return true;
            }

            var result =
                OffsetCompare( d.FailedTunnelTest, 20, d.SuccessfulTunnelTest, 2 ) ||
                OffsetCompare( d.TunnelBuildTimeout, 60, d.SuccessfulTunnelMember, 3 ) ||
                OffsetCompare( d.FailedConnects, 20, d.SuccessfulConnects, 2 );

            return result;
        }

        internal HashSet<I2PIdentHash> GetInactive()
        {
            if ( !Routers.Any() ) return new HashSet<I2PIdentHash>();

            return new HashSet<I2PIdentHash>( 
                Routers.Where( d => NodeInactive( d.Value ) )
                    .Select( d => d.Key ) );
        }

        internal void RemoveOldStatistics( ICollection<I2PIdentHash> keep )
        {
            var toremove = Routers
                .Where( one => !keep.Contains( one.Key ) )
                .ToArray();

            foreach( var one in toremove )
            {
                one.Value.Deleted = true;
            }

            Save();
        }

        public void Remove( I2PIdentHash hash )
        {
            if ( Routers.TryGetValue( hash, out var router ) )
            {
                router.Deleted = true;
            }
        }
    }
}
