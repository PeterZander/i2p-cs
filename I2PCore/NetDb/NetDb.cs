using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;
using Org.BouncyCastle.Utilities.Encoders;
using System.Threading;
using System.Diagnostics;
using I2PCore.Router;
using I2PCore.Tunnel;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Transport;

namespace I2PCore
{
    public class NetDb
    {
        public const int RouterInfoExpiryTimeSeconds = 60 * 60; // From HandleDatabaseLookupMessageJob.java

        const int RouterInfoCountLowWaterMark = 100;

        class RouterInfoMeta
        {
            public int StoreIx;
            public bool Updated;
            public bool Deleted;

            public RouterInfoMeta()
            {
                StoreIx = -1;
            }

            public RouterInfoMeta( int storeix )
            {
                StoreIx = storeix;
            }
        }

        Dictionary<I2PIdentHash, KeyValuePair<I2PRouterInfo, RouterInfoMeta>> RouterInfos = new Dictionary<I2PIdentHash, KeyValuePair<I2PRouterInfo, RouterInfoMeta>>();
        TimeWindowDictionary<I2PIdentHash, I2PLeaseSet> LeaseSets =
            new TimeWindowDictionary<I2PIdentHash, I2PLeaseSet>( I2PLease.LeaseLifetime * 2 );
        Dictionary<I2PString, I2PString> ConfigurationSettings = new Dictionary<I2PString, I2PString>();

        const int DefaultStoreChunkSize = 512;
        enum StoreRecordId : int { StoreIdRouterInfo = 1, StoreIdLeaseSet = 2, StoreIdConfig = 3 };

        protected static Thread Worker;
        public static NetDb Inst { get; protected set; }

        public RoutersStatistics Statistics = new RoutersStatistics();

        RouletteSelection<I2PRouterInfo, I2PIdentHash> Roulette;
        RouletteSelection<I2PRouterInfo, I2PIdentHash> RouletteFloodFill;
        RouletteSelection<I2PRouterInfo, I2PIdentHash> RouletteNonFloodFill;

        public delegate void NetworkDatabaseRouterInfoUpdated( I2PRouterInfo info );
        public delegate void NetworkDatabaseLeaseSetUpdated( I2PLeaseSet ls );
        public delegate void NetworkDatabaseDatabaseSearchReplyReceived( DatabaseSearchReplyMessage dsm );

        public event NetworkDatabaseRouterInfoUpdated RouterInfoUpdates;
        public event NetworkDatabaseLeaseSetUpdated LeaseSetUpdates;
        public event NetworkDatabaseDatabaseSearchReplyReceived DatabaseSearchReplies;

        public readonly IdentResolver IdentHashLookup;

        public readonly FloodfillUpdater FloodfillUpdate = new FloodfillUpdater();

        protected NetDb()
        {
            var dirname = RouterContext.RouterPath;
            if ( !Directory.Exists( dirname ) )
            {
                Directory.CreateDirectory( dirname );
            }
            dirname = NetDbPath;
            if ( !Directory.Exists( dirname ) )
            {
                Directory.CreateDirectory( dirname );
            }

            Worker = new Thread( Run )
            {
                Name = "NetDb",
                IsBackground = true
            };

            IdentHashLookup = new IdentResolver( this );
            Worker.Start();
        }

        ManualResetEvent LoadFinished = new ManualResetEvent( false );
        bool Terminated = false;
        private void Run()
        {
            try
            {
                Logging.LogInformation( "NetDb: Path: " + NetDbPath );
                Logging.Log( "Reading NetDb..." );
                var sw1 = new Stopwatch();
                sw1.Start();
                Load();
                sw1.Stop();
                Logging.Log( string.Format( "Done reading NetDb. {0}. {1} entries.", sw1.Elapsed, RouterInfos.Count ) );

                LoadFinished.Set();
                while ( TransportProvider.Inst == null ) Thread.Sleep( 500 );

                var PeriodicSave = new PeriodicAction( TickSpan.Minutes( 5 ) );

                var PeriodicFFUpdate = new PeriodicAction( TickSpan.Seconds( 5 ) );

                while ( !Terminated )
                {
                    try
                    {
                        PeriodicSave.Do( () => Save( true ) );
                        PeriodicFFUpdate.Do( FloodfillUpdate.Run );
                        IdentHashLookup.Run();
                        Thread.Sleep( 2000 );
                    }
                    catch ( ThreadAbortException ex )
                    {
                        Logging.Log( ex );
                        Terminated = true;
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
            }
            finally
            {
                Terminated = true;
            }
        }

        public static void Start()
        {
            if ( Inst != null ) return;

            Inst = new NetDb();

            if ( !Inst.LoadFinished.WaitOne( 450000 ) )
            {
                Inst.Terminated = true;
                throw new Exception( "NetDb Load did not finish in 450 sec!" );
            }
        }

        public string NetDbPath
        {
            get
            {
				return Path.GetFullPath( Path.Combine( StreamUtils.AppPath, "NetDb" ) );
            }
        }

        public string GetFullPath( string filename )
        {
            return Path.Combine( NetDbPath, filename );
        }

        public string GetFullPath( I2PRouterInfo ri )
        {
            var hash = ri.Identity.IdentHash.Id64;
			return GetFullPath( Path.Combine( $"r{hash[0]}", $"routerInfo-{hash}.dat" ) );
        }

        List<string> GetNetDbFiles()
        {
            var result = new List<string>();

            foreach ( var item in Directory.GetDirectories( NetDbPath ) )
            {
                foreach ( var file in Directory.GetFileSystemEntries( item, "routerInfo-*.dat" ) )
                {
                    result.Add( file );
                }
            }
            return result;
        }

        void Load()
        {
            using ( var s = GetStore() )
            {
                var sw2 = new Stopwatch();
                sw2.Start();
                var ix = 0;
                while ( s != null && ( ix = s.Next( ix ) ) > 0 )
                {
                    var reader = new BufRefLen( s.Read( ix ) );
                    var recordtype = (StoreRecordId)reader.Read32();

                    try
                    {

                        switch ( recordtype )
                        {
                            case StoreRecordId.StoreIdRouterInfo:
                                var one = new I2PRouterInfo( reader, false );
                                var known = RouterInfos.ContainsKey( one.Identity.IdentHash );

                                if ( !ValidateRI( one ) && known )
                                {
                                    s.Delete( RouterInfos[one.Identity.IdentHash].Value.StoreIx );
                                    RouterInfos.Remove( one.Identity.IdentHash );
                                    Statistics.DestinationInformationFaulty( one.Identity.IdentHash );
                                    continue;
                                }

                                RouterInfos[one.Identity.IdentHash] = new KeyValuePair<I2PRouterInfo,RouterInfoMeta>( 
                                    one, 
                                    new RouterInfoMeta( ix ) );
                                break;

                            case StoreRecordId.StoreIdConfig:
                                AccessConfig( delegate( Dictionary<I2PString, I2PString> settings )
                                {
                                    var key = new I2PString( reader );
                                    settings[key] = new I2PString( reader );
                                } );
                                break;

                            default:
                                s.Delete( ix );
                                break;
                        }

                    }
                    catch ( Exception ex )
                    {
                        Logging.LogDebug( $"NetDb: Load: Store exception, ix [{ix}] removed. {ex}" );
                        s.Delete( ix );
                    }
                }
                sw2.Stop();
                Logging.Log( $"Store: {sw2.Elapsed}" );
            }

            var files = GetNetDbFiles();
            foreach ( var file in files )
            {
                AddRouterInfo( file );
            }

            lock ( RouterInfos )
                if ( RouterInfos.Count == 0 )
            {
                Logging.LogWarning( $"WARNING: NetDB database contains no routers. Add router files to {NetDbPath}." );
            }

            Statistics.Load();
            UpdateSelectionProbabilities();

            Save( true );

            foreach ( var file in files )
            {
                File.Delete( file );
            }
        }

        private void UpdateSelectionProbabilities()
        {
            Statistics.UpdateScore();

            lock ( RouterInfos )
            {
                Roulette = new RouletteSelection<I2PRouterInfo, I2PIdentHash>( RouterInfos.Values.Select( p => p.Key ),
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score );

                RouletteFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    RouterInfos.Where( p => p.Value.Key.Options["caps"].Contains( 'f' ) ).Select( ri => ri.Value ).Select( p => p.Key ),
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score );

                RouletteNonFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    RouterInfos.Where( ri => !ri.Value.Key.Options["caps"].Contains( 'f' ) ).Select( ri => ri.Value ).Select( p => p.Key ),
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score );

                Logging.LogInformation( "All routers" );
                ShowRouletteStatistics( Roulette );
                Logging.LogInformation( "Floodfill routers" );
                ShowRouletteStatistics( RouletteFloodFill );
                Logging.LogInformation( "Non floodfill routers" );
                ShowRouletteStatistics( RouletteNonFloodFill );
            }

            Logging.LogDebug( $"Our address: {RouterContext.Inst.ExtAddress} {RouterContext.Inst.TCPPort}/{RouterContext.Inst.UDPPort} {RouterContext.Inst.MyRouterInfo}" );
        }

        private void ShowRouletteStatistics( RouletteSelection<I2PRouterInfo, I2PIdentHash> roulette )
        {
            if ( Logging.LogLevel > Logging.LogLevels.Information ) return;
            if ( !roulette.Wheel.Any() ) return;

            float Mode = 0f;
            var bins = 20;
            var hist = roulette.Wheel.Histogram( sp => sp.Fit, bins, 3f );
            var maxcount = hist.Max( b => b.Count );
            if ( hist.Count() == bins && !hist.All( b => Math.Abs( b.Start ) < 0.01f ) )
            {
                Mode = hist.First( b => b.Count == maxcount ).Start + ( hist[1].Start - hist[0].Start ) / 2f;
            }

            Logging.LogInformation(
                $"Roulette stats: Count {roulette.Wheel.Count()}, " +
                $"Min {roulette.MinFit:0.00}, " +
                $"Avg {roulette.AverageFit:0.00}, " +
                $"Mode {Mode:0.00}, Max {roulette.MaxFit:0.00}, " +
                $"Absdev: {roulette.AbsDevFit:0.00}, " +
                $"Stddev: {roulette.StdDevFit:0.00}, " +
                $"Skew {roulette.Wheel.Skew( sp => sp.Fit ):0.00}" );

            if ( Logging.LogLevel > Logging.LogLevels.Debug ) return;

            var ix = 0;
            if ( maxcount > 0 ) foreach ( var line in hist )
            {
                var st = "";
                for ( int i = 0; i < ( 40 * line.Count ) / maxcount; ++i ) st += "*";
                Logging.LogInformation( $"Roulette stats {line.Start,6:#0.0} ({line.Count,5}): {st}" );
                ++ix;
            }

            var delta = roulette.AbsDevFit / 20;
            var min = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.MinFit ) < delta ).Take( 10 );
            var avg = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.AverageFit ) < delta ).Take( 10 );
            var max = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.MaxFit ) < delta ).Take( 10 );

            if ( min.Any() && avg.Any() && max.Any() )
            {
                var mins = min.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );
                var maxs = max.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );

                Logging.LogDebug( String.Format( "Roulette stats minfit: {0}, maxfit: {1}", mins, maxs ) );

                Logging.LogDebug( $"Min example: {NetDb.Inst.Statistics[min.Random().Id]}" );
                Logging.LogDebug( $"Med example: {NetDb.Inst.Statistics[avg.Random().Id]}" );
                Logging.LogDebug( $"Max example: {NetDb.Inst.Statistics[max.Random().Id]}" );
            }
        }

        private static bool ValidateRI( I2PRouterInfo one )
        {
            return one != null && one.Options.Contains( "caps" ) && one.Adresses.Any( a => a.TransportStyle.Equals( "NTCP" ) || a.TransportStyle.Equals( "SSU" ) );
        }

        private Store GetStore()
        {
            for ( int i = 0; i < 5; ++i )
            {
                try
                {
                    return new Store( GetFullPath( "routerinfo.sto" ), DefaultStoreChunkSize );
                }
                catch ( Exception ex )
                {
                    Logging.Log( "GetStore", ex );
                    System.Threading.Thread.Sleep( 500 );
                }
            }
            return null;
        }

        void Save( bool onlyupdated )
        {
            var created = 0;
            var updated = 0;
            var deleted = 0;

            var sw = new Stopwatch();
            sw.Start();

            var inactive = Statistics.GetInactive();
            RemoveRouterInfo( inactive );

            using ( var s = GetStore() )
            {
                lock ( RouterInfos )
                {
                    foreach ( var one in RouterInfos.ToArray() )
                    {
                        try
                        {
                            if ( one.Value.Value.Deleted )
                            {
                                if ( one.Value.Value.StoreIx > 0 ) s.Delete( one.Value.Value.StoreIx );
                                RouterInfos.Remove( one.Key );
                                ++deleted;
                                continue;
                            }

                            if ( !onlyupdated || ( onlyupdated && one.Value.Value.Updated ) )
                            {
                                var rec = new BufLen[] { (BufLen)(int)StoreRecordId.StoreIdRouterInfo, new BufLen( one.Value.Key.ToByteArray() ) };
                                if ( one.Value.Value.StoreIx > 0 )
                                {
                                    s.Write( rec, one.Value.Value.StoreIx );
                                    ++updated;
                                }
                                else
                                {
                                    one.Value.Value.StoreIx = s.Write( rec );
                                    ++created;
                                }
                                one.Value.Value.Updated = false;
                            }
                        }
                        catch ( Exception ex )
                        {
                            Logging.LogDebug( "NetDb: Save: Store exception: " + ex.ToString() );
                            one.Value.Value.StoreIx = -1;
                        }
                    }
                }

                var lookup = s.GetMatching( e => (StoreRecordId)e[0] == StoreRecordId.StoreIdConfig, 1 );
                Dictionary<I2PString, int> str2ix = new Dictionary<I2PString, int>();
                foreach ( var one in lookup )
                {
                    var reader = new BufRefLen( one.Value );
                    reader.Read32();
                    var key = new I2PString( reader );
                    str2ix[key] = one.Key;
                }

                AccessConfig( delegate( Dictionary<I2PString, I2PString> settings )
                {
                    foreach ( var one in settings )
                    {
                        var rec = new BufLen[] { (BufLen)(int)StoreRecordId.StoreIdConfig, 
                            new BufLen( one.Key.ToByteArray() ), new BufLen( one.Value.ToByteArray() )
                        };

                        if ( str2ix.ContainsKey( one.Key ) )
                        {
                            s.Write( rec, str2ix[one.Key] );
                        }
                        else
                        {
                            s.Write( rec );
                        }
                    }
                } );
            }

            Logging.Log( $"NetDb.Save( {( onlyupdated ? "updated" : "all" )} ): " +
                $"{created} created, {updated} updated, {deleted} deleted." );

            Statistics.RemoveOldStatistics();
            UpdateSelectionProbabilities();

            sw.Stop();
            Logging.Log( $"NetDB: Save: {sw.Elapsed}" );

        }

        public void AddRouterInfo( I2PRouterInfo info )
        {
            if ( !ValidateRI( info ) ) return;

            lock ( RouterInfos )
            {
                if ( RouterInfos.ContainsKey( info.Identity.IdentHash ) )
                {
                    var indb = RouterInfos[info.Identity.IdentHash];

                    if ( ( (DateTime)info.PublishedDate - (DateTime)indb.Key.PublishedDate ).TotalSeconds > 10 )
                    {
                        if ( !info.VerifySignature() )
                        {
                            Logging.LogDebug( $"NetDb: RouterInfo failed signature check: {info.Identity.IdentHash.Id32}" );
                            return;
                        }

                        var meta = indb.Value;
                        meta.Deleted = false;
                        meta.Updated = true;
                        RouterInfos[info.Identity.IdentHash] = new KeyValuePair<I2PRouterInfo,RouterInfoMeta>( info, meta );
                        Logging.LogDebugData( $"NetDb: Updated RouterInfo for: {info.Identity.IdentHash}" );
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if ( !info.VerifySignature() )
                    {
                        Logging.LogDebug( "NetDb: RouterInfo failed signature check: " + info.Identity.IdentHash.Id32 );
                        return;
                    }

                    var meta = new RouterInfoMeta
                    {
                        Updated = true
                    };
                    RouterInfos[info.Identity.IdentHash] = new KeyValuePair<I2PRouterInfo, RouterInfoMeta>( info, meta );
                    Logging.LogDebugData( $"NetDb: Added RouterInfo for: {info.Identity.IdentHash}" );
                }

                if ( RouterInfoUpdates != null ) ThreadPool.QueueUserWorkItem( a => RouterInfoUpdates( info ) );
            }
        }

        public void AddRouterInfo( string file )
        {
            using ( var s = new FileStream( file, FileMode.Open, FileAccess.Read ) )
            {
                var buf = StreamUtils.Read( s );
                var ri = new I2PRouterInfo( new BufRef( buf ), false );

                AddRouterInfo( ri );
            }
        }

        public bool Contains( I2PIdentHash key )
        {
            lock ( RouterInfos )
            {
                if ( RouterInfos.TryGetValue( key, out var pair ) )
                {
                    return !pair.Value.Deleted;
                }
            }
            return false;
        }

        public I2PRouterInfo this[I2PIdentHash key]
        {
            get
            {
                lock ( RouterInfos )
                {
                    if ( RouterInfos.TryGetValue( key, out var pair ) )
                    {
                        if ( pair.Value.Deleted ) return null;
                        return pair.Key;
                    }
                }
                return null;
            }
        }

        public void AddLeaseSet( I2PLeaseSet leaseset )
        {
            LeaseSets[leaseset.Destination.IdentHash] = leaseset;
            if ( LeaseSetUpdates != null ) ThreadPool.QueueUserWorkItem( a => LeaseSetUpdates( leaseset ) );
        }

        public I2PLeaseSet FindLeaseSet( I2PIdentHash dest )
        {
            return LeaseSets[dest];
        }

        public IEnumerable<I2PRouterInfo> Find( IEnumerable<I2PIdentHash> hashes )
        {
            foreach ( var key in hashes )
            {
                lock ( RouterInfos )
                {
                    if ( RouterInfos.TryGetValue( key, out var result ) ) yield return result.Key;
                }
            }
        }

        Random Rnd = new Random();

        private I2PIdentHash GetRandomRouter( 
            RouletteSelection<I2PRouterInfo, I2PIdentHash> r,
            IEnumerable<I2PIdentHash> exclude,
            bool exploratory )
        {
            I2PIdentHash result;
            var me = RouterContext.Inst.MyRouterIdentity.IdentHash;

            var excludeset = new HashSet<I2PIdentHash>( exclude );

            if ( exploratory )
            {
                lock ( RouterInfos )
                {
                    var subset = RouterInfos
                            .Where( k => !excludeset.Contains( k.Key ) );
                    do
                    {
                        result = subset
                            .Random()
                            .Key;
                    } while ( result == me );

                    return result;
                }
            }

            var retries = 0;
            bool tryagain;
            do
            {
                result = r.GetWeightedRandom( excludeset );
                tryagain = result == me;
            } while ( tryagain && ++retries < 20 );

            //Logging.LogInformation( $"GetRandomRouter selected {result}: {exclude.Any( k2 => k2 == result )}" );

            return result;
        }

        I2PRouterInfo GetRandomRouterInfo( RouletteSelection<I2PRouterInfo, I2PIdentHash> r, bool exploratory )
        {
            return this[GetRandomRouter( r, Enumerable.Empty<I2PIdentHash>(), exploratory )];
        }

        public I2PRouterInfo GetRandomRouterInfo( bool exploratory )
        {
            return GetRandomRouterInfo( Roulette, exploratory );
        }

        private ItemFilterWindow<I2PIdentHash> RecentlyUsedForTunnel = new ItemFilterWindow<I2PIdentHash>( TickSpan.Minutes( 7 ), 2 );

        public I2PIdentHash GetRandomRouterForTunnelBuild( bool exploratory )
        {
            I2PIdentHash result;

            result = GetRandomRouter( Roulette, RecentlyUsedForTunnel, exploratory );
            RecentlyUsedForTunnel.Update( result );

            return result;
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
            return GetRandomRouter( RouletteFloodFill, RecentlyUsedForFF, exploratory );
        }

        public IEnumerable<I2PIdentHash> GetRandomFloodfillRouter( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i ) yield return GetRandomFloodfillRouter( exploratory );
        }

        public IEnumerable<I2PRouterInfo> GetRandomFloodfillRouterInfo( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i ) yield return GetRandomFloodfillRouterInfo( exploratory );
        }

        public IEnumerable<I2PRouterInfo> GetRandomNonFloodfillRouterInfo( bool exploratory, int count )
        {
            for ( int i = 0; i < count; ++i ) yield return GetRandomNonFloodfillRouterInfo( exploratory );
        }

        public IEnumerable<I2PRouterInfo> GetClosestNonFloodfill( I2PIdentHash reference, int count, List<I2PIdentHash> exclude )
        {
            lock ( RouletteNonFloodFill.Wheel )
            {
                //var subset = RouletteNonFloodFill.WheelAverageOrBetter;
                var subset = RouletteFloodFill.Wheel.AsEnumerable();
                if ( exclude != null && exclude.Any() ) subset = subset.Where( inf => !exclude.Any( excl => excl == inf.Id ) );
                subset = subset.Where( p => NetDb.Inst.Statistics[p.Id].Score >= RouletteNonFloodFill.AverageFit );
                lock ( RouterInfos )
                {
                    subset = subset.Where( inf => RouterInfos.ContainsKey( inf.Id ) );
                }
                subset = subset.OrderBy( p => p.Id ^ reference.RoutingKey );
                return Find( subset.Take( count ).Select( p => p.Id ) ).ToArray();
            }
        }

        public IEnumerable<I2PIdentHash> GetClosestFloodfill( I2PIdentHash reference, int count, IList<I2PIdentHash> exclude, bool nextset )
        {
            lock ( RouletteFloodFill.Wheel )
            {
                //var subset = RouletteFloodFill.WheelAverageOrBetter;
                var subset = RouletteFloodFill.Wheel.AsEnumerable();

                if ( exclude != null && exclude.Any() ) subset = subset.Where( inf => !exclude.Any( excl => excl == inf.Id ) );

                var refkey = nextset ? reference.NextRoutingKey : reference.RoutingKey;

                /*
                var subsetlim = count / 4;
                if ( subsetlim < 250 && subset.Count() > 5 * count )
                {
                    if ( nextset )
                    {
                        var testsubset = subset.Where( p => ( p.Id[0] ^ refkey[0] ) <= subsetlim );
                        // Found a smaller subset to sort?
                        if ( testsubset.Count() >= count ) subset = testsubset;
                    }
                    else
                    {
                        var testsubset = subset.Where( p => ( p.Id[0] ^ refkey[0] ) <= subsetlim );
                        // Found a smaller subset to sort?
                        if ( testsubset.Count() >= count ) subset = testsubset;
                    }
                }*/

                if ( nextset )
                {
                    subset = subset.OrderBy( p => p.Id ^ refkey );
                }
                else
                {
                    subset = subset.OrderBy( p => p.Id ^ refkey );
                }
                return subset.Take( count ).Select( p => p.Id );
            }
        }

        public IEnumerable<I2PRouterInfo> GetClosestFloodfillInfo( I2PIdentHash reference, int count, List<I2PIdentHash> exclude, bool nextset )
        {
            return Find( GetClosestFloodfill( reference, count, exclude, nextset ) );
        }

        public void RemoveRouterInfo( I2PIdentHash hash )
        {
            lock ( RouterInfos )
            {
                if ( RouterInfos.ContainsKey( hash ) )
                {
                    RouterInfos[hash].Value.Deleted = true;
                }
            }
        }

        public void RemoveRouterInfo( IEnumerable<I2PIdentHash> hashes )
        {
            lock ( RouterInfos )
            {
                foreach ( var hash in hashes )
                {
                    if ( RouterInfos.ContainsKey( hash ) )
                    {
                        RouterInfos[hash].Value.Deleted = true;
                    }
                }
            }
        }

        public void AddDatabaseSearchReply( DatabaseSearchReplyMessage dbsr )
        {
            if ( DatabaseSearchReplies != null ) ThreadPool.QueueUserWorkItem( a => DatabaseSearchReplies( dbsr ) );
        }

        public delegate void ConfigAccessFunction( Dictionary<I2PString, I2PString> settings );
        public void AccessConfig( ConfigAccessFunction fcn )
        {
            try
            {
                lock ( ConfigurationSettings )
                {
                    fcn( ConfigurationSettings );
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( "Exception in AccessConfig callback" );
                Logging.Log( ex );
            }
        }

        public IEnumerable<I2PRouterInfo> FindRouterInfo( Func<I2PIdentHash,I2PRouterInfo,bool> filter )
        {
            lock ( RouterInfos )
            {
                return RouterInfos
                    .Where( ri => filter( ri.Key, ri.Value.Key ) )
                    .Select( ri => ri.Value.Key )
                    .ToArray();
            }
        }

    }
}
