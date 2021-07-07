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

        public void IdentResolveRITimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.IdentResolveRITimeout ), false );
        }

        public void IdentResolveLSTimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.IdentResolveLSTimeout ), false );
        }

        public void IdentResolveSuccess( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.IdentResolveSuccess ), true );
        }

        public void IdentResolveReply( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.IdentResolveReply ), true );
        }

        public void IsFirewalledUpdate( I2PIdentHash hash, bool isfw )
        {
            Update( hash, ds => ds.IsFirewalled = isfw, true );
        }

        internal static float BandwidthMax = 1f;

        public void UpdateScore()
        {
            var rar = Routers.ToArray();

            if ( !rar.Any() ) return;

            BandwidthMax = rar.Max( r => r.Value.MaxBandwidthSeen );
            if ( float.IsNaN( BandwidthMax ) || BandwidthMax < 1f ) BandwidthMax = 1f;

            foreach ( var one in rar ) one.Value.UpdateScore();
        }

        bool OffsetCompare( double fail, double offset, double success, double multip )
        {
            return fail - offset > multip * success;
        }

#if DEBUG
        ConcurrentDictionary<string,int> NodeInactiveReason = new ConcurrentDictionary<string,int>();
        PeriodicAction ReportInactiveReason = new PeriodicAction( TickSpan.Minutes( 7 ) );
        HashSet<RouterStatistics> InactiveReasonAlreadyReported = new HashSet<RouterStatistics>();
        bool UpdateInactiveStatistics;

        void AddInactiveReason( string reason )
        {
            var nirc = NodeInactiveReason.GetOrAdd( reason, 0 );
            NodeInactiveReason[reason] = nirc + 1;
        }
#endif

        bool TestInactive( Func<bool> test, string desc )
        {
            var result = test();
#if DEBUG            
            if ( UpdateInactiveStatistics && result ) AddInactiveReason( desc );
#endif      
            return result;      
        }
        public bool NodeInactive( RouterStatistics d )
        {
            if ( d.InformationFaulty > 0 ) 
            {
                return true;
            }

            var result = false;
#if DEBUG
            UpdateInactiveStatistics = !InactiveReasonAlreadyReported.Contains( d );
#endif

            result |= TestInactive( 
                        () => OffsetCompare( d.FloodfillUpdateTimeout, 5, d.FloodfillUpdateSuccess, 2 ),
                        "FloodfillUpdateTimeout" );

            result |= TestInactive( 
                        () => OffsetCompare( d.FailedTunnelTest, 20, d.SuccessfulTunnelTest, 3 ),
                        "FailedTunnelTest" );

            result |= TestInactive( 
                        () => OffsetCompare( d.TunnelBuildTimeout, 50, d.SuccessfulTunnelMember, 5 ),
                        "TunnelBuildTimeout" );

            result |= TestInactive( 
                        () => OffsetCompare( d.IdentResolveRITimeout, 50, d.IdentResolveSuccess + d.IdentResolveReply * 0.7, 5 ),
                        "IdentResolveTimeout" );

            result |= TestInactive( 
                        () => OffsetCompare( d.FailedConnects, 2, d.SuccessfulConnects, 1.5 ),
                        "FailedConnects" );

#if DEBUG
            if ( result && UpdateInactiveStatistics )
            {
                InactiveReasonAlreadyReported.Add( d );
                UpdateInactiveStatistics = false;
            }
#endif

            return result;
        }

        internal HashSet<I2PIdentHash> GetInactive()
        {
            if ( !Routers.Any() ) return new HashSet<I2PIdentHash>();

            var result = new HashSet<I2PIdentHash>( 
                Routers.Where( d => NodeInactive( d.Value ) )
                    .Select( d => d.Key ) );

#if DEBUG
            ReportInactiveReason.Do( () => 
            {
                var items = NodeInactiveReason
                                .OrderByDescending( p => p.Value )
                                .ToArray();

                var sum = items.Sum( p => p.Value ) / 100.0;

                var sta = items.Select( p => $" {p.Key}: {p.Value} ({p.Value / sum:F1}%)" );
                var line = $"RoutersStatistics: NodeInactiveReason:{string.Join( ',', sta )}";
                Logging.LogDebug( line );
            } );
#endif            

            return result;
        }

        internal void RemoveOldStatistics( ICollection<I2PIdentHash> keep )
        {
            var now = DateTime.UtcNow;

            var toremove = Routers
                .Where( one => ( now - (DateTime)one.Value.LastSeen ).TotalDays > 2
                            && !keep.Contains( one.Key ) )
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
