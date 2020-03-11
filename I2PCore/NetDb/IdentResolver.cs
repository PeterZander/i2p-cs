using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TransportLayer;
using I2PCore.Data;
using System.Threading;
using System.Collections.Concurrent;

namespace I2PCore
{
    public class IdentResolver
    {
        public readonly TickSpan DatabaseLookupWaitTime = TickSpan.Seconds( 10 );
        public const int DatabaseLookupRetries = 3;
        public const int DatabaseLookupSelectFloodfillCount = 2;

        PeriodicAction CheckForTimouts = new PeriodicAction( TickSpan.Seconds( 3 ) );
        PeriodicAction ExploreNewRouters = new PeriodicAction( TickSpan.Seconds( 45 ) );

        public delegate void IdentResolverResultFail( I2PIdentHash key );
        public delegate void IdentResolverResultRouterInfo( I2PRouterInfo ri );
        public delegate void IdentResolverResultLeaseSet( I2PLeaseSet ls );

        public event IdentResolverResultFail LookupFailure;
        public event IdentResolverResultRouterInfo RouterInfoReceived;
        public event IdentResolverResultLeaseSet LeaseSetReceived;

        class IdentUpdateRequestInfo
        {
            public readonly TickCounter Start = TickCounter.Now;
            public readonly I2PIdentHash IdentKey;
            public readonly DatabaseLookupMessage.LookupTypes LookupType;

            public int WaitingFor;
            public int Retries;
            public TickCounter LastQuery = TickCounter.MaxDelta;

            public ConcurrentBag<I2PIdentHash> AlreadyQueried = new ConcurrentBag<I2PIdentHash>();

            public IdentUpdateRequestInfo( I2PIdentHash id, DatabaseLookupMessage.LookupTypes lookuptype )
            {
                IdentKey = id;
                LookupType = lookuptype;
                Retries = 0;
                WaitingFor = DatabaseLookupSelectFloodfillCount;
            }
        }

        ConcurrentDictionary<I2PIdentHash, IdentUpdateRequestInfo> OutstandingQueries = 
                new ConcurrentDictionary<I2PIdentHash, IdentUpdateRequestInfo>();

        public IdentResolver( NetDb db )
        {
            db.RouterInfoUpdates += NetDb_RouterInfoUpdates;
            db.LeaseSetUpdates += NetDb_LeaseSetUpdates;
            db.DatabaseSearchReplies += NetDb_DatabaseSearchReplies;
        }

        public void LookupRouterInfo( I2PIdentHash ident )
        {
            if ( OutstandingQueries.ContainsKey( ident ) )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.LogDebug( $"IdentResolver: Lookup of RouterInfo {ident.Id32Short} already in progress." );
#endif
                return;
            }

            var newitem = new IdentUpdateRequestInfo( ident, DatabaseLookupMessage.LookupTypes.RouterInfo );
#if LOG_ALL_IDENT_LOOKUPS
            Logging.Log( $"IdentResolver: Starting lookup of RouterInfo for {ident.Id32Short}." );
#endif
            OutstandingQueries[ident] = newitem;

            SendRIDatabaseLookup( ident, newitem );
        }

        public void LookupLeaseSet( I2PIdentHash ident )
        {
            if ( OutstandingQueries.ContainsKey( ident ) )
            {
                Logging.LogDebug( $"IdentResolver: Lookup of LeaseSet {ident.Id32Short} already in progress." );
                return;
            }

            var newitem = new IdentUpdateRequestInfo( ident, DatabaseLookupMessage.LookupTypes.LeaseSet );

            Logging.LogDebug( $"IdentResolver: Starting lookup of LeaseSet for {ident.Id32Short}." );
            OutstandingQueries[ident] = newitem;

            SendLSDatabaseLookup( ident, newitem );
        }

        void NetDb_DatabaseSearchReplies( DatabaseSearchReplyMessage dsm )
        {
#if LOG_ALL_IDENT_LOOKUPS
            StringBuilder foundrouters = new StringBuilder();
#else
            string foundrouters = "";
#endif

            foreach ( var router in dsm.Peers )
            {
#if LOG_ALL_IDENT_LOOKUPS
                foundrouters.AppendFormat( "{0}{1}", ( foundrouters.Length != 0 ? ", " : "" ), router.Id32Short );
#endif
                if ( NetDb.Inst.Contains( router ) )
                {
#if LOG_ALL_IDENT_LOOKUPS
                    Logging.Log( $"IdentResolver: Not looking up RouterInfo {router.Id32Short} from SearchReply as its already known." );
#endif
                    continue;
                }
                LookupRouterInfo( router );
            }

            if ( !OutstandingQueries.TryGetValue( dsm.Key, out var info ) ) return;

            ++info.Retries;
            --info.WaitingFor;

            if ( info.WaitingFor > 0 ) return;

            if ( info.Retries <= DatabaseLookupRetries )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} resulted in alternative servers to query {2}. Retrying.",
                    ( info.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo ? "RouterInfo" : "LeaseSet" ),
                    dsm.Key.Id32Short, foundrouters ) );
#endif

                if ( info.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo )
                {
                    SendRIDatabaseLookup( info.IdentKey, info );
                    info.LastQuery = TickCounter.Now;
                }
                else
                {
                    SendLSDatabaseLookup( info.IdentKey, info );
                    info.LastQuery = TickCounter.Now;
                }
            }
            else
            {
                Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} resulted in alternative server to query {2}. Lookup failed.",
                    ( info.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo ? "RouterInfo" : "LeaseSet" ),
                    dsm.Key.Id32Short, foundrouters ) );

                OutstandingQueries.TryRemove( dsm.Key, out _ );
                if ( LookupFailure != null ) ThreadPool.QueueUserWorkItem( a => LookupFailure( dsm.Key ) );
            }
        }

        void NetDb_LeaseSetUpdates( I2PLeaseSet ls )
        {
            if ( !OutstandingQueries.TryRemove( ls.Destination.IdentHash, out var info ) ) return;

            Logging.Log( string.Format( "IdentResolver: Lookup of LeaseSet {0} succeeded. {1}", 
                ls.Destination.IdentHash.Id32Short, info.Start.DeltaToNow ) );
            if ( LeaseSetReceived != null ) ThreadPool.QueueUserWorkItem( a => LeaseSetReceived( ls ) );
        }

        void NetDb_RouterInfoUpdates( I2PRouterInfo ri )
        {
            if ( !OutstandingQueries.TryRemove( ri.Identity.IdentHash, out var info ) ) return;

            Logging.Log( string.Format( "IdentResolver: Lookup of RouterInfo {0} succeeded. {1}", 
                ri.Identity.IdentHash.Id32Short, info.Start.DeltaToNow ) );
            if ( RouterInfoReceived != null ) ThreadPool.QueueUserWorkItem( a => RouterInfoReceived( ri ) );
        }

        public void Run()
        {
            CheckForTimouts.Do( CheckTimeouts );
            ExploreNewRouters.Do( ExplorationRouterLookup );
        }

        private void SendRIDatabaseLookup( I2PIdentHash ident, IdentUpdateRequestInfo info )
        {
            var ff = NetDb.Inst.GetClosestFloodfill( 
                ident, 
                10 + 3 * info.Retries, 
                info.AlreadyQueried, 
                false );

            if ( !ff.Any() )
            {
                Logging.Log( $"IdentResolver: failed to find a floodfill router to lookup ({ident}): " );
                return;
            }

            ff.Shuffle();
            ff = ff.Take( DatabaseLookupSelectFloodfillCount ).ToArray();

            foreach ( var oneff in ff )
            {
                try
                {
                    var msg = new DatabaseLookupMessage(
                                ident,
                                RouterContext.Inst.MyRouterIdentity.IdentHash,
                                DatabaseLookupMessage.LookupTypes.RouterInfo );

                    TransportProvider.Send( oneff, msg );
#if LOG_ALL_IDENT_LOOKUPS
                    Logging.Log( $"IdentResolver: RouterInfo query {msg.Key.Id32Short} sent to {oneff.Id32Short}" );
#endif
                }
                catch ( Exception ex )
                {
                    Logging.Log( "SendRIDatabaseLookup", ex );
                }
            }

            foreach( var f in ff )
            {
                info.AlreadyQueried.Add( f );
            }
        }

        private void SendLSDatabaseLookup( I2PIdentHash ident, IdentUpdateRequestInfo info )
        {
            var outtunnel = TunnelProvider.Inst.GetEstablishedOutboundTunnel( true );
            var replytunnel = TunnelProvider.Inst.GetInboundTunnel( true );

            if ( outtunnel is null || replytunnel is null )
            {
                Logging.LogDebug( $"SendLSDatabaseLookup: " +
                    $"outtunnel: {outtunnel}, replytunnel: {replytunnel}" );
                return;
            }

            var getnext = DateTime.UtcNow.Hour >= 23;
            var ff = NetDb.Inst.GetClosestFloodfill( 
                    ident, 
                    DatabaseLookupSelectFloodfillCount + 2 * info.Retries, 
                    info.AlreadyQueried, 
                    getnext )
                .Select( r => new { Id = r, NetDb.Inst.Statistics[r].Score } );

#if LOG_ALL_IDENT_LOOKUPS
            StringBuilder foundrouters = new StringBuilder();
            StringBuilder foundrouterskeys = new StringBuilder();

            foreach ( var router in ff )
            {
                foundrouters.AppendFormat( "{0}{1}{2}", ( foundrouters.Length != 0 ? ", " : "" ), 
                    router.Id32Short, 
                    FreenetBase64.Encode( router.Hash ) );
                foundrouterskeys.AppendFormat( "{0}{1}{2}", ( foundrouterskeys.Length != 0 ? ", " : "" ), 
                    router.Id32Short,
                    FreenetBase64.Encode( router.RoutingKey.Hash ) );
            }
            var st = foundrouters.ToString();
            var st2 = foundrouterskeys.ToString();
#endif

            if ( !ff.Any() )
            {
                Logging.Log( $"IdentResolver failed to find a floodfill router to lookup ({ident}): " );
                return;
            }

            var minscore = ff.Min( r => r.Score );

            for ( int i = 0; i < DatabaseLookupSelectFloodfillCount; ++i )
            {
                var oneff = ff
                        .RandomWeighted( r => r.Score - minscore + 0.1 )
                        .Id;
                try
                {
                    var msg = new DatabaseLookupMessage(
                                ident,
                                replytunnel.Destination, replytunnel.GatewayTunnelId,
                                DatabaseLookupMessage.LookupTypes.LeaseSet, null );

                    outtunnel.Send( new TunnelMessageRouter( msg, oneff ) );

                    info.AlreadyQueried.Add( oneff );

#if !LOG_ALL_IDENT_LOOKUPS
                    Logging.Log( $"IdentResolver: LeaseSet query {msg.Key.Id32Short} " +
                        $"sent to {oneff.Id32Short}. Dist: {oneff ^ msg.Key.RoutingKey}" );
#endif
                }
                catch ( Exception ex )
                {
                    Logging.Log( "SendLSDatabaseLookup", ex );
                }
            }
        }

        /*
         Exploration

         Exploration is a special form of netdb lookup, where a router attempts to learn about new routers. 
         It does this by sending a floodfill router a I2NP DatabaseLookupMessage, looking for a random key. 
         As this lookup will fail, the floodfill would normally respond with a I2NP DatabaseSearchReplyMessage 
         containing hashes of floodfill routers close to the key. This would not be helpful, as the requesting 
         router probably already knows those floodfills, and it would be impractical to add ALL floodfill 
         routers to the "don't include" field of the lookup. For an exploration query, the requesting router 
         adds a router hash of all zeros to the "don't include" field of the DatabaseLookupMessage. 
         
         The floodfill will then respond only with non-floodfill routers close to the requested key.
         
         https://geti2p.net/en/docs/how/network-database
         * 
            11  => exploration lookup, return DatabaseSearchReplyMessage
                    containing non-floodfill routers only (replaces an
                    excludedPeer of all zeroes)   
         https://geti2p.net/spec/i2np#databaselookup
         */
        void ExplorationRouterLookup()
        {
            I2PIdentHash ident = new I2PIdentHash( true );

            var ff = NetDb.Inst.GetClosestFloodfill( ident, 10, null, false )
                    .Shuffle()
                    .Take( DatabaseLookupSelectFloodfillCount )
                    .ToArray();

            foreach ( var oneff in ff )
            {
                var msg = new DatabaseLookupMessage(
                            ident,
                            RouterContext.Inst.MyRouterIdentity.IdentHash,
                            DatabaseLookupMessage.LookupTypes.Exploration,
                            new I2PIdentHash[] { new I2PIdentHash( false ) } );

#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( "IdentResolver: Random router lookup " + ident.Id32Short + " sent to " + oneff.Id32Short );
#endif
                try
                {
                    TransportProvider.Send( oneff, msg );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        void CheckTimeouts()
        {
            var timeout = OutstandingQueries.Where( i => 
                    i.Value.Start.DeltaToNow > DatabaseLookupWaitTime 
                    && i.Value.Retries >= DatabaseLookupRetries )
                .Select( i => i.Value )
                .ToArray();

            foreach ( var item in timeout ) OutstandingQueries.TryRemove( item.IdentKey, out _ );

            var retry = OutstandingQueries.Where( i => 
                    i.Value.Start.DeltaToNow > DatabaseLookupWaitTime )
                .Select( i => i.Value )
                .ToArray();

            foreach ( var one in timeout )
            {
                Logging.Log( string.Format( "IdentResolver: {0} lookup {1} failed with timeout.",
                    ( one.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo ? "RouterInfo" : "LeaseSet" ), 
                    one.IdentKey.Id32Short ) );

                if ( LookupFailure != null ) ThreadPool.QueueUserWorkItem( a => LookupFailure( one.IdentKey ) );
            }

            foreach ( var one in retry )
            {
                ++one.Retries;

#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( string.Format( "IdentResolver: {0} lookup {1} failed with timeout Retry {2}.",
                    ( one.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo ? "RouterInfo" : "LeaseSet" ),
                    one.IdentKey.Id32Short, one.Retries ) );
#endif
                if ( one.LookupType == DatabaseLookupMessage.LookupTypes.RouterInfo )
                {
                    SendRIDatabaseLookup( one.IdentKey, one );
                    one.LastQuery = TickCounter.Now;
                }
                else
                {
                    SendLSDatabaseLookup( one.IdentKey, one );
                    one.LastQuery = TickCounter.Now;
                }
            }
        }
    }
}
