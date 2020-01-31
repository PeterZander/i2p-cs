using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.Threading;
using I2PCore.Utils;
using System.Diagnostics;
using System.Net;

namespace I2PCore
{
    public class MTUConfig
    {
        public const int BufferSize = 1484 - 28;

        public int MTU;
        public int MTUMax;
        public int MTUMin;
    }

    public interface IMTUProvider
    {
        MTUConfig GetMTU( IPEndPoint ep );
        void MTUUsed( IPEndPoint ep, MTUConfig mtu );
    }

    public class SessionStatistics: IMTUProvider
    {
        enum StoreRecordId : int { DestinationStatistics = 1 };
        Dictionary<I2PIdentHash, DestinationStatistics> Destinations = new Dictionary<I2PIdentHash, DestinationStatistics>();

        public DestinationStatistics this[ I2PIdentHash ix ]
        {
            get
            {
                lock ( Destinations )
                {
                    DestinationStatistics stat;

                    if ( !Destinations.ContainsKey( ix ) )
                    {
                        stat = new DestinationStatistics( ix );
                        Destinations[ix] = stat;
                    }
                    else
                    {
                        stat = Destinations[ix];
                    }
                    return stat;
                }
            }
        }

        private Store GetStore()
        {
            return new Store( NetDb.Inst.GetFullPath( "statistics.sto" ), -1 );
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
                        case StoreRecordId.DestinationStatistics:
                            constrsw.Start();
                            var one = new DestinationStatistics( reader );
                            constrsw.Stop();

                            dicsw.Start();
                            Destinations[one.Id] = one;
                            dicsw.Stop();

                            one.StoreIx = ix;
                            break;

                        default:
                            s.Delete( ix );
                            break;
                    }
                }
                sw2.Stop();
                Logging.Log( string.Format( "Statistics load: Total: {0}, Read(): {1}, Constr: {2}, Dict: {3} ", 
                    sw2.Elapsed, 
                    readsw.Elapsed, 
                    constrsw.Elapsed,
                    dicsw.Elapsed ) );
            }
        }

        const float twoweeks = 1000 * 60 * 60 * 24 * 14;

        public void Save()
        {
            var sw2 = new Stopwatch();
            sw2.Start();
            var deleted = 0;
            using ( var s = GetStore() )
            {
                lock ( Destinations )
                {
                    if ( !Destinations.Any() ) return;

                    var avgageage = Destinations.Average( ds => (double)(ulong)ds.Value.LastSeen );

                    foreach ( var one in Destinations.ToArray() )
                    {
                        if ( avgageage - (ulong)one.Value.LastSeen > twoweeks )
                        {
                            if ( one.Value.StoreIx > 0 )
                            {
                                s.Delete( one.Value.StoreIx );
                                one.Value.StoreIx = -1;
                            }
                            Destinations.Remove( one.Key );
                            ++deleted;
                            continue;
                        }

                        var rec = new BufLen[] { (BufLen)(int)StoreRecordId.DestinationStatistics, new BufLen( one.Value.ToByteArray() ) };

                        if ( one.Value.StoreIx > 0 )
                        {
                            s.Write( rec, one.Value.StoreIx );
                        }
                        else
                        {
                            one.Value.StoreIx = s.Write( rec );
                        }
                    }
                }
            }
            sw2.Stop();
            Logging.Log( "Statistics save: " + sw2.Elapsed.ToString() + ", " + deleted.ToString() + " deleted." );
        }

        public delegate void Accessor( DestinationStatistics ds );

        public void Update( I2PIdentHash target, Accessor acc, bool success )
        {
            var rec = this[target];
            if ( success ) rec.LastSeen = new I2PDate( DateTime.UtcNow );
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

        // Not answering a build request is wose than declining
        public void TunnelBuildTimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.TunnelBuildTimeout ), false );
        }

#if DEBUG
        float msph = 10000;
        PeriodicLogger LogHopBuild = new PeriodicLogger( 30 );
#endif

        public void TunnelBuildTimeMsPerHop( I2PIdentHash hash, long ms )
        {
#if DEBUG
            msph = ( msph * 49f + (float)ms ) / 50f;
            LogHopBuild.Log( () => "Tunnel build time ema: " + msph.ToString( "F0" ) );
#endif
            Update( hash, ds => ds.TunnelBuildTimeMsPerHop = Math.Min( ds.TunnelBuildTimeMsPerHop, ms ), true );
        }

        public void FloodfillUpdateTimeout( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FloodfillUpdateTimeout ), false );
        }

        public void FloodfillUpdateSuccess( I2PIdentHash hash )
        {
            Update( hash, ds => Interlocked.Increment( ref ds.FloodfillUpdateSuccess ), false );
        }

        public void MTUUsed( IPEndPoint ep, MTUConfig mtu )
        {
        }

        public void UpdateScore()
        {
            lock ( Destinations )
            {
                foreach ( var one in Destinations.ToArray() ) one.Value.UpdateScore();
            }
        }

        float ScoreAverage = 0f;
        float ScoreMaxStdDev = 5f;

        public void UpdateScoreAverages( float avg, float stddev )
        {
            ScoreAverage = avg;

            if ( ScoreMaxStdDev * 1.1f < stddev )
            {
                ScoreMaxStdDev = stddev;

                Logging.LogDebug( string.Format( "SessionStatistics: UpdateScoreAverages: ScoreMaxStdDev updated {0:0.00}", ScoreMaxStdDev ) );
            }
        }

        const double oneweek = 1000d * 60 * 60 * 24 * 7;

        internal IEnumerable<I2PIdentHash> GetInactive()
        {
            lock ( Destinations )
            {
                if ( !Destinations.Any() ) return Enumerable.Empty<I2PIdentHash>();

                var recent = Destinations.Average( d => (double)(ulong)d.Value.LastSeen );
                return Destinations.Where( d => 
                    d.Value.Score < ScoreAverage - 2f * ScoreMaxStdDev ||
                    recent - (double)(ulong)d.Value.LastSeen > oneweek ||
                    ( d.Value.FailedTunnelTest > 2 && d.Value.FailedTunnelTest > 2 * d.Value.SuccessfulTunnelTest ) ||
                    ( d.Value.FailedConnects > 3 && d.Value.FailedConnects > 2 * d.Value.SuccessfulConnects )
                    ).Select( d => d.Key ).ToArray();
            }
        }

        internal void RemoveInactive()
        {
            lock ( Destinations )
            {
                foreach( var one in GetInactive() )
                {
                    Destinations.Remove( one );
                }
            }
        }

        public void Remove( I2PIdentHash hash )
        {
            lock ( Destinations )
            {
                Destinations.Remove( hash );
            }
        }

        public MTUConfig GetMTU( IPEndPoint ep )
        {
            var result = new MTUConfig();

            if ( ep == null )
            {
                result.MTU = 1484 - 28; // IPV4 28 byte UDP header
                result.MTUMax = 1484 - 28;
                result.MTUMin = 620 - 28;
                return result;
            }

            switch ( ep.AddressFamily )
            {
                case System.Net.Sockets.AddressFamily.InterNetwork:
                    result.MTU = 1484 - 28;
                    result.MTUMax = 1484 - 28;
                    result.MTUMin = 620 - 28;
                    break;

                case System.Net.Sockets.AddressFamily.InterNetworkV6:
                    result.MTU = 1280 - 48;  // IPV6 48 byte UDP header
                    result.MTUMax = 1472 - 48;
                    result.MTUMin = 1280 - 48;
                    break;

                default:
                    throw new NotImplementedException( ep.AddressFamily.ToString() + " not supported" );
            }

            return result;
        }
    }
}
