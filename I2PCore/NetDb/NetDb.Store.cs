using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Data;
using System.IO;
using I2PCore.Utils;
using System.Diagnostics;
using I2PCore.SessionLayer;
using static System.Configuration.ConfigurationManager;

namespace I2PCore
{
    public partial class NetDb
    {
        const int DefaultStoreChunkSize = 512;
        enum StoreRecordId : int { StoreIdRouterInfo = 1, StoreIdLeaseSet = 2, StoreIdConfig = 3 };

        protected class RouterInfoMeta
        {
            public int StoreIx;
            public bool Updated;
            public bool Deleted;
            public I2PIdentHash Id;

            public RouterInfoMeta( I2PIdentHash id )
            {
                Id = id;
                StoreIx = -1;
            }

            public RouterInfoMeta( int storeix )
            {
                StoreIx = storeix;
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

        private Store GetStore()
        {
            return BufUtils.GetStore( 
                GetFullPath( "routerinfo.sto" ), 
                DefaultStoreChunkSize );
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

                                if ( !ValidateRI( one ) )
                                {
                                    s.Delete( ix );
                                    RouterInfos.TryRemove( one.Identity.IdentHash, out _ );
                                    FloodfillInfos.TryRemove( one.Identity.IdentHash, out _ );
                                    Statistics.DestinationInformationFaulty( one.Identity.IdentHash );

                                    continue;
                                }

                                if ( !RouterContext.Inst.UseIpV6 )
                                {
                                    if ( !one.Adresses.Any( a => a.Options.ValueContains( "host", "." ) ) )
                                    {
                                        Logging.LogDebug( $"NetDb: RouterInfo have no IPV4 address: {one.Identity.IdentHash.Id32}" );
                                        s.Delete( ix );

                                        continue;
                                    }
                                }

                                var re = new RouterEntry(
                                    one,
                                    new RouterInfoMeta( ix ) );
                                RouterInfos[one.Identity.IdentHash] = re;
                                if ( re.IsFloodfill ) FloodfillInfos[one.Identity.IdentHash] = re;
                                break;

                            case StoreRecordId.StoreIdConfig:
                                AccessConfig( delegate ( Dictionary<I2PString, I2PString> settings )
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

            ImportNetDbFiles();

            if ( RouterInfos.Count < 20 )
            {
                Logging.LogWarning( $"WARNING: NetDB database contains " +
                    $"{RouterInfos.Count} routers. Add router files to {NetDbPath}." );

                DoBootstrap();
            }

            Statistics.Load();
            IsFirewalledUpdate();

#if DEBUG
            ShowDebugDatabaseInfo();
#endif

            UpdateSelectionProbabilities();

            Save( true );
        }

        void Save( bool onlyupdated )
        {
            var created = 0;
            var updated = 0;
            var deleted = 0;

            var sw = new Stopwatch();
            sw.Start();

            RemoveOldRouterInfos();

            using ( var s = GetStore() )
            {
                foreach ( var one in RouterInfos.ToArray() )
                {
                    try
                    {
                        if ( one.Value.Meta.Deleted )
                        {
                            if ( one.Value.Meta.StoreIx > 0 ) s.Delete( one.Value.Meta.StoreIx );
                            RouterInfos.TryRemove( one.Key, out _ );
                            FloodfillInfos.TryRemove( one.Key, out _ );
                            ++deleted;
                            continue;
                        }

                        if ( !onlyupdated || ( onlyupdated && one.Value.Meta.Updated ) )
                        {
                            var rec = new BufLen[] 
                            { 
                                BufUtils.To32BL( (int)StoreRecordId.StoreIdRouterInfo ), 
                                new BufLen( one.Value.Router.ToByteArray() ) 
                            };

                            if ( one.Value.Meta.StoreIx > 0 )
                            {
                                s.Write( rec, one.Value.Meta.StoreIx );
                                ++updated;
                            }
                            else
                            {
                                one.Value.Meta.StoreIx = s.Write( rec );
                                ++created;
                            }
                            one.Value.Meta.Updated = false;
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.LogDebug( "NetDb: Save: Store exception: " + ex.ToString() );
                        one.Value.Meta.StoreIx = -1;
                    }
                }

                SaveConfig( s );
            }

            Logging.Log( $"NetDb.Save( {( onlyupdated ? "updated" : "all" )} ): " +
                $"{created} created, {updated} updated, {deleted} deleted." );

            Statistics.RemoveOldStatistics();
            UpdateSelectionProbabilities();

            sw.Stop();
            Logging.Log( $"NetDB: Save: {sw.Elapsed}" );
        }

        private void SaveConfig( Store s )
        {
            var lookup = s.GetMatching( e => (StoreRecordId)e[0] == StoreRecordId.StoreIdConfig, 1 );
            var str2ix = new Dictionary<I2PString, int>();
            foreach ( var one in lookup )
            {
                var reader = new BufRefLen( one.Value );
                reader.Read32();
                var key = new I2PString( reader );
                str2ix[key] = one.Key;
            }

            AccessConfig( delegate ( Dictionary<I2PString, I2PString> settings )
            {
                foreach ( var one in settings )
                {
                    var rec = new BufLen[] { 
                        BufUtils.To32BL( (int)StoreRecordId.StoreIdConfig ),
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

        private void RemoveOldRouterInfos()
        {
            var inactive = Statistics.GetInactive();

            var now = DateTime.UtcNow;
            var old = RouterInfos
                .Select( ri => new 
                    {
                        Id = ri.Key,
                        Router = ri.Value.Router,
                        Days = ( now - (DateTime)ri.Value.Router.PublishedDate ).TotalDays 
                    } )
                .Where( info => info.Days > 1.0 )
                .ToArray();

            inactive.UnionWith( old.Select( info => info.Id ) );

            if ( RouterInfos.Count - inactive.Count < 400 )
            {
                inactive = new HashSet<I2PIdentHash>(
                    inactive
                        .Where( k => RouterInfos.ContainsKey( k ) )
                        .OrderBy( k => (DateTime)RouterInfos[k].Router.PublishedDate )
                        .Take( RouterInfos.Count - 400 ) );
            }

            RemoveRouterInfo( inactive );
        }

        private void ImportNetDbFiles()
        {
            var importfiles = GetNetDbFiles();
            foreach ( var file in importfiles )
            {
                AddRouterInfo( file );
            }

            foreach ( var file in importfiles )
            {
                File.Delete( file );
            }
        }

        private void IsFirewalledUpdate()
        {
            var fw = RouterInfos.Where( ri =>
                ri.Value.Router.Adresses.Any( a =>
                    a.Options.Any( o =>
                        o.Key.ToString() == "ihost0" ) ) );

            foreach ( var ri in fw )
            {
                Statistics.IsFirewalledUpdate( ri.Key, true );
            }
        }

        private void DoBootstrap()
        {
            var imported = 0;

            if ( !string.IsNullOrWhiteSpace( AppSettings["ReseedFile"] ) )
            {
                var filename = AppSettings["ReseedFile"];
                imported += Bootstrap.FileBootstrap( filename );
            }

            if ( imported == 0 )
            {
                var t = Bootstrap.NetworkBootstrap();
                t.ConfigureAwait( false );
                imported += t.Result;
            }
        }
    }
}