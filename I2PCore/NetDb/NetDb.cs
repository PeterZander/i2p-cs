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
        const float RouletteMaxScaleFactor = 20f;

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

        public SessionStatistics Statistics = new SessionStatistics();

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

        protected NetDb()
        {
            Worker = new Thread( () => Run() );
            Worker.Name = "NetDb";
            Worker.IsBackground = true;

            IdentHashLookup = new IdentResolver( this );
            Worker.Start();
        }

        ManualResetEvent LoadFinished = new ManualResetEvent( false );
        bool Terminated = false;
        private void Run()
        {
            try
            {
                DebugUtils.LogInformation( "NetDb: Path: " + NetDbPath );
                DebugUtils.Log( "Reading NetDb..." );
                var sw1 = new Stopwatch();
                sw1.Start();
                Load();
                sw1.Stop();
                DebugUtils.Log( string.Format( "Done reading NetDb. {0}. {1} entries.", sw1.Elapsed, RouterInfos.Count ) );

                LoadFinished.Set();
                while ( TransportProvider.Inst == null ) Thread.Sleep( 500 );

                var PeriodicSave = new PeriodicAction( TickSpan.Minutes( 5 ) );

                var PeriodicFFUpdate = new PeriodicAction( TickSpan.Seconds( 5 ) );
                var FloodfillUpdate = new FloodfillUpdater();

                while ( !Terminated )
                {
                    try
                    {
                        PeriodicSave.Do( () => Save( true ) );
                        PeriodicFFUpdate.Do( () => FloodfillUpdate.Run() );
                        IdentHashLookup.Run();
                        Thread.Sleep( 2000 );
                    }
                    catch ( ThreadAbortException ex )
                    {
                        DebugUtils.Log( ex );
                        Terminated = true;
                    }
                    catch ( Exception ex )
                    {
                        DebugUtils.Log( ex );
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
                return Path.GetFullPath( StreamUtils.AppPath + "NetDb\\" );
            }
        }

        public string GetFullPath( string filename )
        {
            return Path.Combine( NetDbPath, filename );
        }

        public string GetFullPath( I2PRouterInfo ri )
        {
            var hash = ri.Identity.IdentHash.Id64;
            return GetFullPath( "r" + hash[0] + "\\routerInfo-" + hash + ".dat" );
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
                while ( ( ix = s.Next( ix ) ) > 0 )
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
                        DebugUtils.LogDebug( "NetDb: Load: Store exception, ix [" + ix.ToString() + "] removed. " + ex.ToString() );
                        s.Delete( ix );
                    }
                }
                sw2.Stop();
                DebugUtils.Log( "Store: " + sw2.Elapsed.ToString() );
            }

            var files = GetNetDbFiles();
            foreach ( var file in files )
            {
                AddRouterInfo( file );
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
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score, RouletteMaxScaleFactor );

                Statistics.UpdateScoreAverages( Roulette.AverageFit, Roulette.StdDevFit );

                RouletteFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    RouterInfos.Where( p => p.Value.Key.Options["caps"].Contains( 'f' ) ).Select( ri => ri.Value ).Select( p => p.Key ),
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score, RouletteMaxScaleFactor );

                RouletteNonFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    RouterInfos.Where( ri => !ri.Value.Key.Options["caps"].Contains( 'f' ) ).Select( ri => ri.Value ).Select( p => p.Key ),
                    ih => ih.Identity.IdentHash, i => Statistics[i].Score, RouletteMaxScaleFactor );

                DebugUtils.LogInformation( "All routers" );
                ShowRouletteStatistics( Roulette );
                DebugUtils.LogInformation( "Floodfill routers" );
                ShowRouletteStatistics( RouletteFloodFill );
                DebugUtils.LogInformation( "Non floodfill routers" );
                ShowRouletteStatistics( RouletteNonFloodFill );
            }
        }

        private void ShowRouletteStatistics( RouletteSelection<I2PRouterInfo, I2PIdentHash> roulette )
        {
            if ( DebugUtils.LogLevel > DebugUtils.LogLevels.Information ) return;

            float Mode = 0f;
            var bins = 20;
            var hist = roulette.Wheel.Histogram( sp => sp.Fit, bins );
            var maxcount = hist.Max( b => b.Count );
            if ( hist.Count() == bins && !hist.All( b => Math.Abs( b.Start ) < 0.01f ) )
            {
                Mode = hist.Where( b => b.Count == maxcount ).First().Start + ( hist[1].Start - hist[0].Start ) / 2f;
            }

            DebugUtils.LogInformation( string.Format( "Roulette stats: Count {3}, Min {0:0.00}, Avg {1:0.00}, Mode {6:0.00}, Max {2:0.00}, Stddev: {5:0.00}, Skew {4:0.00}",
                roulette.MinFit,
                roulette.AverageFit,
                roulette.MaxFit,
                roulette.Wheel.Count,
                roulette.Wheel.Skew( sp => sp.Fit ),
                roulette.StdDevFit,
                Mode ) );

            if ( DebugUtils.LogLevel > DebugUtils.LogLevels.Debug ) return;

            var ix = 0;
            if ( maxcount > 0 ) foreach ( var line in hist )
                {
                    var st = "";
                    for ( int i = 0; i < ( 40 * line.Count ) / maxcount; ++i ) st += "*";
                    DebugUtils.LogDebug( String.Format( "Roulette stats {0,6:#0.0} ({2,5}): {1}", line.Start, st, line.Count ) );
                    ++ix;
                }

            var aobminval = roulette.WheelAverageOrBetter.Min( sp => sp.Fit );
            DebugUtils.LogDebug( String.Format( "Roulette WheelAverageOrBetter count: {0}, minfit: {1}", roulette.WheelAverageOrBetter.Count(), aobminval ) );

            var min = roulette.Wheel.Where( sp => sp.Fit == roulette.MinFit ).Take( 10 );
            var avg = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.AverageFit ) < roulette.StdDevFit * 0.3 ).Take( 10 );
            var max = roulette.Wheel.Where( sp => sp.Fit == roulette.MaxFit ).Take( 10 );

            if ( min.Any() && avg.Any() && max.Any() )
            {
                var mins = min.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );
                var maxs = max.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );

                DebugUtils.LogDebug( String.Format( "Roulette stats minfit: {0}, maxfit: {1}", mins, maxs ) );

                DebugUtils.LogDebug( "Min example: " + NetDb.Inst.Statistics[min.Random().Id].ToString() );
                DebugUtils.LogDebug( "Med example: " + NetDb.Inst.Statistics[avg.Random().Id].ToString() );
                DebugUtils.LogDebug( "Max example: " + NetDb.Inst.Statistics[max.Random().Id].ToString() );
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
                    DebugUtils.Log( "GetStore", ex );
                    System.Threading.Thread.Sleep( 500 );
                }
            }
            return null;
        }

        void Save( bool onlyupdated )
        {
            var deleted = 0;
            var saved = 0;

            var sw = new Stopwatch();
            sw.Start();

            var inactive = Statistics.GetInactive();
            if ( RouterInfos.Count - inactive.Count() <= RouterInfoCountLowWaterMark )
            {
                inactive = inactive.Take( RouterInfos.Count - RouterInfoCountLowWaterMark );
            }
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
                                }
                                else
                                {
                                    one.Value.Value.StoreIx = s.Write( rec );
                                }
                                one.Value.Value.Updated = false;
                                ++saved;
                            }
                        }
                        catch ( Exception ex )
                        {
                            DebugUtils.LogDebug( "NetDb: Save: Store exception: " + ex.ToString() );
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

            DebugUtils.Log( string.Format( "NetDb.Save( {1} ): {0} entries saved, {2} deleted.", 
                saved, 
                onlyupdated ? "updated" : "all",
                deleted ) );

            Statistics.Save();
            UpdateSelectionProbabilities();

            sw.Stop();
            DebugUtils.Log( "NetDB: Save: " + sw.Elapsed.ToString() );
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
                            DebugUtils.LogDebug( "NetDb: RouterInfo failed signature check: " + info.Identity.IdentHash.Id32 );
                            return;
                        }

                        var meta = indb.Value;
                        meta.Deleted = false;
                        meta.Updated = true;
                        RouterInfos[info.Identity.IdentHash] = new KeyValuePair<I2PRouterInfo,RouterInfoMeta>( info, meta );
                        DebugUtils.Log( "NetDb: Updated RouterInfo for: " + info.Identity.IdentHash );
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
                        DebugUtils.LogDebug( "NetDb: RouterInfo failed signature check: " + info.Identity.IdentHash.Id32 );
                        return;
                    }

                    var meta = new RouterInfoMeta();
                    meta.Updated = true;
                    RouterInfos[info.Identity.IdentHash] = new KeyValuePair<I2PRouterInfo, RouterInfoMeta>( info, meta );
                    DebugUtils.Log( "NetDb: Added RouterInfo for: " + info.Identity.IdentHash );
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
                KeyValuePair<I2PRouterInfo, RouterInfoMeta> pair;
                if ( RouterInfos.TryGetValue( key, out pair ) )
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
                    KeyValuePair<I2PRouterInfo, RouterInfoMeta> pair;
                    if ( RouterInfos.TryGetValue( key, out pair ) )
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
                    KeyValuePair<I2PRouterInfo, RouterInfoMeta> result;
                    if ( RouterInfos.TryGetValue( key, out result ) ) yield return result.Key;
                }
            }
        }

        Random Rnd = new Random();

        I2PIdentHash GetRandomRouter( RouletteSelection<I2PRouterInfo, I2PIdentHash> r, bool exploratory )
        {
            I2PIdentHash result;
            var me = RouterContext.Inst.MyRouterIdentity.IdentHash;

            if ( exploratory ) lock ( RouterInfos )
                {
                    do
                    {
                        lock ( r.Wheel )
                        {
                            result = Roulette.Wheel.Random().Id;
                        }
                    } while ( result == me );

                    return result;
                }

            do
            {
                var one = r.GetWeightedRandom();
                var retries = 0;
                while ( !Contains( one ) && ++retries < 20 ) one = r.GetWeightedRandom();
                result = one;
            } while ( result == me );

            return result;
        }

        I2PRouterInfo GetRandomRouterInfo( RouletteSelection<I2PRouterInfo, I2PIdentHash> r, bool exploratory )
        {
            return this[GetRandomRouter( r, exploratory )];
        }

        public I2PRouterInfo GetRandomRouterInfo( bool exploratory )
        {
            return GetRandomRouterInfo( Roulette, exploratory );
        }

        ItemFilterWindow<I2PIdentHash> RecentlyUsedForTunnel = new ItemFilterWindow<I2PIdentHash>( TickSpan.Minutes( 12 ), 1 );

        public I2PRouterInfo GetRandomRouterInfoForTunnelBuild( bool exploratory )
        {
            I2PRouterInfo result;

            int retries = 0;
            do
            {
                result = GetRandomRouterInfo( exploratory );
            } while ( ++retries < 100 && !RecentlyUsedForTunnel.Test( result.Identity.IdentHash ) );

            RecentlyUsedForTunnel.Update( result.Identity.IdentHash );
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

        public I2PIdentHash GetRandomFloodfillRouter( bool exploratory )
        {
            return GetRandomRouter( RouletteFloodFill, exploratory );
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
            foreach( var hash in hashes ) RemoveRouterInfo( hash );
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
                DebugUtils.Log( "Exception in AccessConfig callback" );
                DebugUtils.Log( ex );
            }
        }
    }
}
