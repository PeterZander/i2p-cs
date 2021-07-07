using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;
using System.Threading;
using System.Diagnostics;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TransportLayer;
using System.Collections.Concurrent;

namespace I2PCore
{
    public partial class NetDb
    {
        // From HandleDatabaseLookupMessageJob.java
        public static readonly TickSpan RouterInfoExpiryTime = TickSpan.Minutes( 60 );

        const int RouterInfoCountLowWaterMark = 100;
        static int RouletteIncludeTopField = 3000;
        public static int RouletteIncludeTop
        {
            get
            {
                return RouletteIncludeTopField;
            }
            set
            {
                var elitism = RouletteElitism;
                RouletteIncludeTopField = value;
                RouletteElitism = elitism;
            }
        }

        
        /// <summary>
        /// The how many times more probable the highest scoring router is choosen than the lowest
        /// scoring router, if the number of routers are equal to RouletteIncludeTop.
        /// If the number of routers are less than RouletteIncludeTop, the elitism is reduced
        /// linearely. See: "Fitness proportionate selection"
        /// </summary>
        public static double RouletteElitism
        {
            get
            {
                return Math.Pow( RouletteElitismIncrement, ( RouletteIncludeTop - 1 ) );
            }
            set
            {
                RouletteElitismIncrement = Math.Pow( value, 1.0 / ( RouletteIncludeTop - 1 ) );
            }
        }

        // Defaults to 50000x
        public static double RouletteElitismIncrement = Math.Pow( 50000.0, 1.0 / RouletteIncludeTop );

        ConcurrentDictionary<I2PIdentHash, RouterEntry> RouterInfos = 
                new ConcurrentDictionary<I2PIdentHash, RouterEntry>();

        ConcurrentDictionary<I2PIdentHash, RouterEntry> FloodfillInfos =
                new ConcurrentDictionary<I2PIdentHash, RouterEntry>();

        TimeWindowDictionary<I2PIdentHash, ILeaseSet> LeaseSets =
            new TimeWindowDictionary<I2PIdentHash, ILeaseSet>( I2PLease.LeaseLifetime * 2 );
        Dictionary<I2PString, I2PString> ConfigurationSettings = new Dictionary<I2PString, I2PString>();

        protected static Thread Worker;
        public static NetDb Inst { get; protected set; }

        public RoutersStatistics Statistics = new RoutersStatistics();

        RouletteSelection<I2PRouterInfo, I2PIdentHash> Roulette;
        RouletteSelection<I2PRouterInfo, I2PIdentHash> RouletteFloodFill;
        RouletteSelection<I2PRouterInfo, I2PIdentHash> RouletteNonFloodFill;

        public delegate void NetworkDatabaseRouterInfoUpdated( I2PRouterInfo info );
        public delegate void NetworkDatabaseLeaseSetUpdated( ILeaseSet ls );
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
                Logging.LogInformation( $"NetDb: Path: {NetDbPath}" );
                Logging.Log( "Reading NetDb..." );
                var sw1 = new Stopwatch();
                sw1.Start();
                Load();
                sw1.Stop();
                Logging.Log( $"Done reading NetDb. {sw1.Elapsed}. {RouterInfos.Count} entries." );

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

        private void UpdateSelectionProbabilities()
        {
            Statistics.UpdateScore();

            var havehost = RouterInfos.Values.Where( rp =>
                rp.Router.Adresses.Any( a =>
                    a.Options.Contains( "host" ) ) );

            Roulette = new RouletteSelection<I2PRouterInfo, I2PIdentHash>( 
                    havehost.Select( p => p.Router ),
                    ih => ih.Identity.IdentHash, 
                    i => Statistics[i].Score,
                    RouletteIncludeTop,
                    RouletteElitismIncrement );

            RouletteFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    FloodfillInfos.Select( rp => rp.Value.Router ),
                    ih => ih.Identity.IdentHash,
                    i => Statistics[i].Score,
                    RouletteIncludeTop,
                    RouletteElitismIncrement );

            RouletteNonFloodFill = new RouletteSelection<I2PRouterInfo, I2PIdentHash>(
                    havehost.Where( ri => !ri.IsFloodfill )
                        .Select( ri => ri.Router ),
                    ih => ih.Identity.IdentHash, 
                    i => Statistics[i].Score,
                    RouletteIncludeTop,
                    RouletteElitismIncrement );

            Logging.LogInformation( "All routers" );
            ShowRouletteStatistics( Roulette );
            Logging.LogInformation( "Floodfill routers" );
            ShowRouletteStatistics( RouletteFloodFill );
            Logging.LogInformation( "Non floodfill routers" );
            ShowRouletteStatistics( RouletteNonFloodFill );

#if SHOW_PROBABILITY_PROFILE
            ShowProbabilityProfile();
#endif

            Logging.LogDebug( $"Our address: {RouterContext.Inst.ExtIPV4Address} {RouterContext.Inst.TCPPort}/{RouterContext.Inst.UDPPort} {RouterContext.Inst.MyRouterInfo}" );
        }

        private static bool ValidateRI( I2PRouterInfo one )
        {
            return one != null 
                && one.Options
                    .Contains( "caps" );
        }

        public bool AddRouterInfo( I2PRouterInfo info )
        {
            if ( !ValidateRI( info ) )
            {
                Logging.LogDebugData( $"NetDb: RouterInfo failed validation: {info}" );
                return false;
            }

            Statistics.IsFirewalledUpdate( info.Identity.IdentHash,
                info.Adresses.Any( a =>
                    a.Options.Any( o =>
                        o.Key.ToString() == "ihost0" ) )
            );

            if ( RouterInfos.TryGetValue( info.Identity.IdentHash, out var indb ) )
            {
                if ( ( (DateTime)info.PublishedDate - (DateTime)indb.Router.PublishedDate ).TotalSeconds > 2 )
                {
                    if ( !info.VerifySignature() )
                    {
                        Logging.LogDebug( $"NetDb: RouterInfo failed signature check: {info.Identity.IdentHash.Id32}" );
                        return false;
                    }

                    var meta = indb.Meta;
                    meta.Deleted = false;
                    meta.Updated = true;
                    var re = new RouterEntry( info, meta );
                    RouterInfos[info.Identity.IdentHash] = re;
                    if ( re.IsFloodfill ) FloodfillInfos[info.Identity.IdentHash] = re;
                    Logging.LogDebugData( $"NetDb: Updated RouterInfo for: {info.Identity.IdentHash}" );
                }
                else
                {
                    return true;
                }
            }
            else
            {
                if ( !info.VerifySignature() )
                {
                    Logging.LogDebug( $"NetDb: RouterInfo failed signature check: {info.Identity.IdentHash.Id32}" );
                    return false;
                }

                var meta = new RouterInfoMeta( info.Identity.IdentHash )
                {
                    Updated = true
                };
                var re = new RouterEntry( info, meta );
                RouterInfos[info.Identity.IdentHash] = re;
                if ( re.IsFloodfill ) FloodfillInfos[info.Identity.IdentHash] = re;
                Logging.LogDebugData( $"NetDb: Added RouterInfo for: {info.Identity.IdentHash}" );

                Statistics.IsFirewalledUpdate( 
                        info.Identity.IdentHash, 
                        info.Adresses
                            .Any( a =>
                                a.Options.Any( o =>
                                    o.Key.ToString() == "ihost0" ) ) );
            }

            if ( RouterInfoUpdates != null ) ThreadPool.QueueUserWorkItem( a => RouterInfoUpdates( info ) );

            return true;
        }

        public bool AddRouterInfo( string file )
        {
            using ( var s = new FileStream( file, FileMode.Open, FileAccess.Read ) )
            {
                return AddRouterInfo( s );
            }
        }

        public bool AddRouterInfo( Stream s )
        {
            var buf = StreamUtils.Read( s );
            var ri = new I2PRouterInfo( new BufRef( buf ), false );

            return AddRouterInfo( ri );
        }

        public bool Contains( I2PIdentHash key )
        {
            if ( RouterInfos.TryGetValue( key, out var pair ) )
            {
                return !pair.Meta.Deleted;
            }
            return false;
        }

        public I2PRouterInfo this[I2PIdentHash key]
        {
            get
            {
                if ( key is null ) return null;

                if ( RouterInfos.TryGetValue( key, out var pair ) )
                {
                    if ( pair?.Meta?.Deleted ?? true )
                    {
                        Logging.LogDebug( $"NetDb[]: Meta is null." );
                        return null;
                    }

                    return pair.Router;
                }
                return null;
            }
        }

        public void AddLeaseSet( ILeaseSet leaseset )
        {
            if ( LeaseSets.TryGetValue( leaseset.Destination.IdentHash, out var extls ) )
            {
                if ( extls.Expire > leaseset.Expire )
                {
                    Logging.LogDebug( $"NetDb: AddLeaseSet: Discarding as we already have a later version" );
                    return;
                }
            }

#if DEBUG
            var lifetime = leaseset.Expire - DateTime.UtcNow;
            if ( lifetime.TotalMinutes < 2 )
            {
                Logging.LogDebug( $"NetDb: AddLeaseSet: Leases are about to expire in ({lifetime})" );
            }
#endif

            LeaseSets[leaseset.Destination.IdentHash] = leaseset;

            if ( LeaseSetUpdates != null ) ThreadPool.QueueUserWorkItem( a => LeaseSetUpdates( leaseset ) );
        }

        public ILeaseSet FindLeaseSet( I2PIdentHash dest )
        {
            return LeaseSets[dest];
        }

        public static bool AreLeasesGood( ILeaseSet ls )
        {
            if ( ls?.Leases?.Any() ?? false )
            {
                return ls.Expire > DateTime.UtcNow + TimeSpan.FromMinutes( 4 );
            }

            return false;
        }

        public IEnumerable<I2PRouterInfo> Find( IEnumerable<I2PIdentHash> hashes )
        {
            foreach ( var key in hashes )
            {
                if ( RouterInfos.TryGetValue( key, out var result ) ) yield return result.Router;
            }
        }

        public void RemoveRouterInfo( I2PIdentHash hash )
        {
            if ( RouterInfos.TryGetValue( hash, out var p ) )
            {
                p.Meta.Deleted = true;
            }
            FloodfillInfos.TryRemove( hash, out _ );
        }

        public void RemoveRouterInfo( IEnumerable<I2PIdentHash> hashes )
        {
            foreach ( var hash in hashes )
            {
                if ( RouterInfos.TryGetValue( hash, out var p ) )
                {
                    p.Meta.Deleted = true;
                }
                FloodfillInfos.TryRemove( hash, out _ );
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
            return RouterInfos
                .Where( ri => filter( ri.Key, ri.Value.Router ) )
                .Select( ri => ri.Value.Router )
                .ToArray();
        }
    }
}
