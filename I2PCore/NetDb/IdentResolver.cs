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
        public const int DatabaseLookupRetriesRI = 4;
        public const int DatabaseLookupRetriesLS = 20;
        public const int DatabaseLookupSelectFloodfillCountRI = 1;
        public const int DatabaseLookupSelectFloodfillCountLS = 2;
        public static TickSpan WaitForRouterInfo = TickSpan.Seconds( 10 );
        public static TickSpan WaitForLeaseSet = TickSpan.Seconds( 20 );

        PeriodicAction CheckForTimouts = new PeriodicAction( TickSpan.Seconds( 3 ) );
        PeriodicAction ExploreNewRouters = new PeriodicAction( TickSpan.Seconds( 45 ) );

        public delegate void IdentResolverResultFail( I2PIdentHash key );
        public delegate void IdentResolverResultRouterInfo( I2PRouterInfo ri );
        public delegate void IdentResolverResultLeaseSet( ILeaseSet ls );

        public event IdentResolverResultFail LookupFailure;
        public event IdentResolverResultRouterInfo RouterInfoReceived;
        public event IdentResolverResultLeaseSet LeaseSetReceived;

        public enum ReceivedFloodfillResponses { NoResponse, Timeout, SearchReply, DatabaseStore }
        public class FloodfillResponse
        {
            public ReceivedFloodfillResponses Response = ReceivedFloodfillResponses.NoResponse;
            public I2PIdentHash Floodfill;
        }

        protected class IdentUpdateRequestInfo
        {
            public readonly TickCounter Start = TickCounter.Now;
            public readonly TickSpan DatabaseLookupWaitTime;
            public readonly I2PIdentHash LookupIdent;
            public readonly DatabaseLookupMessage.LookupTypes LookupType;

            public int Retries;
            public ConcurrentDictionary<I2PIdentHash,FloodfillResponse> FloodfillResponses { get; protected set; }

            public static TimeWindowDictionary<I2PIdentHash,object> AlreadyQueried = 
                            new TimeWindowDictionary<I2PIdentHash, object>( TickSpan.Minutes( 3 ) );

            public IdentUpdateRequestInfo( 
                I2PIdentHash id,
                DatabaseLookupMessage.LookupTypes lookuptype )
            {
                FloodfillResponses = new ConcurrentDictionary<I2PIdentHash,FloodfillResponse>();
                LookupIdent = id;
                LookupType = lookuptype;
                Retries = 0;

                switch ( lookuptype )
                {
                    case DatabaseLookupMessage.LookupTypes.RouterInfo:
                        DatabaseLookupWaitTime = WaitForRouterInfo;
                        break;

                    case DatabaseLookupMessage.LookupTypes.Normal:
                    case DatabaseLookupMessage.LookupTypes.LeaseSet:
                        DatabaseLookupWaitTime = WaitForLeaseSet;
                        break;
                }
            }

            public void StartLookup( IEnumerable<I2PIdentHash> floodfills )
            {
                FloodfillResponses = new ConcurrentDictionary<I2PIdentHash,FloodfillResponse>(
                    floodfills.Select( ff => new KeyValuePair<I2PIdentHash, FloodfillResponse>(
                        ff,
                        new FloodfillResponse() ) ) );
                Start.SetNow();
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

        public bool LookupRouterInfo( I2PIdentHash ident )
        {
            bool inprogress = true;

            var updateinfo = OutstandingQueries.GetOrAdd(
                    ident,
                    ( id ) =>
                    {
                        inprogress = false;
                        return new IdentUpdateRequestInfo(
                                ident,
                                DatabaseLookupMessage.LookupTypes.RouterInfo );
                    } );

            if ( inprogress )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.LogDebug( $"IdentResolver: Lookup of RouterInfo {ident.Id32Short} already in progress." );
#endif
                return false;
            }

#if LOG_ALL_IDENT_LOOKUPS
            Logging.Log( $"IdentResolver: Starting lookup of RouterInfo for {ident.Id32Short}." );
#endif

            SendRIDatabaseLookup( ident, updateinfo );

            return true;
        }

        public bool LookupLeaseSet( I2PIdentHash ident )
        {
            bool inprogress = true;

            var updateinfo = OutstandingQueries.GetOrAdd(
                    ident,
                    ( id ) =>
                    {
                        inprogress = false;
                        return new IdentUpdateRequestInfo(
                                ident,
                                DatabaseLookupMessage.LookupTypes.LeaseSet );
                    } );

            if ( inprogress )
            {
                Logging.LogDebug( $"IdentResolver: Lookup of LeaseSet {ident.Id32Short} already in progress." );
                return false;
            }

            Logging.LogDebug( $"IdentResolver: Starting lookup of LeaseSet for {ident.Id32Short}." );
            SendLSDatabaseLookup( ident, updateinfo );

            return true;
        }

        void NetDb_DatabaseSearchReplies( DatabaseSearchReplyMessage dsm )
        {
#if LOG_ALL_IDENT_LOOKUPS
            StringBuilder foundrouters = new StringBuilder();
#else
            var foundrouters = dsm?.Peers?.Count;
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

            var isleaseset = info.LookupType == DatabaseLookupMessage.LookupTypes.LeaseSet;

            // Collect router performance
            if ( info.FloodfillResponses.TryGetValue( dsm.From, out var resp )
                    && resp.Response == ReceivedFloodfillResponses.NoResponse )
            {
                NetDb.Inst.Statistics.IdentResolveReply( dsm.From );

                info.FloodfillResponses[dsm.From]
                    = new FloodfillResponse()
                    {
                        Floodfill = dsm.From,
                        Response = ReceivedFloodfillResponses.SearchReply
                    };
            }
            else
            {
                var noresponse = info.FloodfillResponses
                        .Where( r => r.Value.Response == ReceivedFloodfillResponses.NoResponse );

                // Someone is lying
                foreach( var one in noresponse )
                {
                    var from = one.Key;
                    
                    if ( isleaseset )
                    {
                        SendLSDatabaseLookup( info.LookupIdent, info );
                    }
                    else
                    {
                        SendRIDatabaseLookup( info.LookupIdent, info );
                    }

                    info.FloodfillResponses[from]
                        = new FloodfillResponse()
                        {
                            Floodfill = from,
                            Response = ReceivedFloodfillResponses.Timeout
                        };
                }
            }

            ++info.Retries;

            if ( info.Retries <= ( isleaseset ? DatabaseLookupRetriesLS : DatabaseLookupRetriesRI ) )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} resulted in alternative servers to query '{2}'. Retrying.",
                    ( isleaseset ? "LeaseSet" : "RouterInfo" ),
                    dsm.Key.Id32Short, foundrouters ) );
#endif

                if ( isleaseset )
                {
                    SendLSDatabaseLookup( info.LookupIdent, info );
                }
                else
                {
                    SendRIDatabaseLookup( info.LookupIdent, info );
                }
            }
            else
            {
                Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} resulted in alternative servers to query '{2}'. Lookup failed.",
                    ( isleaseset ? "LeaseSet" : "RouterInfo" ),
                    dsm.Key.Id32Short, foundrouters ) );

                OutstandingQueries.TryRemove( dsm.Key, out _ );
                if ( LookupFailure != null ) ThreadPool.QueueUserWorkItem( a => LookupFailure( dsm.Key ) );
            }
        }

        void NetDb_LeaseSetUpdates( ILeaseSet ls )
        {
            if ( !OutstandingQueries.TryRemove( ls.Destination.IdentHash, out var info ) )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( $"IdentResolver: Lookup of LeaseSet " +
                    $"{ls.Destination.IdentHash.Id32Short} succeeded " +
                    $"but is no longer monitored." );
#endif
                return;
            }

            // Collect router performance
            var noresponse = info.FloodfillResponses
                    .Where( r => r.Value.Response == ReceivedFloodfillResponses.NoResponse );

            // Give all the credit
            foreach( var one in noresponse )
            {
                var from = one.Key;

                NetDb.Inst.Statistics.IdentResolveSuccess( from );

                info.FloodfillResponses[from]
                    = new FloodfillResponse()
                    {
                        Floodfill = from,
                        Response = ReceivedFloodfillResponses.DatabaseStore
                    };
            }

            if ( ls.Expire < DateTime.UtcNow )
            {
                Logging.LogDebug( $"IdentResolver: Lookup of LeaseSet " +
                    $"{ls} succeeded, but has expired. {info.Start.DeltaToNow}" );

                SendRetries( new IdentUpdateRequestInfo[] { info } );

                return;
            }

            Logging.Log( $"IdentResolver: Lookup of LeaseSet " +
                $"{ls.Destination.IdentHash.Id32Short} succeeded. {info.Start.DeltaToNow}" );

            if ( LeaseSetReceived != null ) ThreadPool.QueueUserWorkItem( a => LeaseSetReceived( ls ) );
        }

        void NetDb_RouterInfoUpdates( I2PRouterInfo ri )
        {
            if ( !OutstandingQueries.TryRemove( ri.Identity.IdentHash, out var info ) )
            {
#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( $"IdentResolver: Lookup of RouterInfo " +
                    $"{ri.Identity.IdentHash.Id32Short} succeeded " +
                    $"but is no longer monitored." );
#endif
                return;
            }

            // Collect router performance
            var noresponse = info.FloodfillResponses
                    .Where( r => r.Value.Response == ReceivedFloodfillResponses.NoResponse );

            // Give all the credit
            foreach( var one in noresponse )
            {
                var from = one.Key;
                
                NetDb.Inst.Statistics.IdentResolveSuccess( from );

                info.FloodfillResponses[from]
                    = new FloodfillResponse()
                    {
                        Floodfill = from,
                        Response = ReceivedFloodfillResponses.DatabaseStore
                    };
            }

            Logging.Log( $"IdentResolver: Lookup of RouterInfo " +
                $"{ri.Identity.IdentHash.Id32Short} succeeded. {info.Start.DeltaToNow}" );

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
                IdentUpdateRequestInfo.AlreadyQueried.Select( d => d.Key ).ToHashSet() );

            if ( !ff.Any() )
            {
                Logging.Log( $"IdentResolver: failed to find a floodfill router to lookup ({ident}): " );
                return;
            }

            ff.Shuffle();
            ff = ff.Take( DatabaseLookupSelectFloodfillCountRI ).ToArray();

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

                IdentUpdateRequestInfo.AlreadyQueried[oneff] = 1;
            }

            info.StartLookup( ff );
        }

        private void SendLSDatabaseLookup( I2PIdentHash ident, IdentUpdateRequestInfo info )
        {
            try
            {
            var outtunnel = TunnelProvider.Inst.GetEstablishedOutboundTunnel( TunnelPoolSelection.RequireExploratory );
            var replytunnel = TunnelProvider.Inst.GetEstablishedInboundTunnel( TunnelPoolSelection.RequireExploratory );

            if ( outtunnel is null || replytunnel is null )
            {
                Logging.LogDebug( $"SendLSDatabaseLookup: " +
                    $"outtunnel: {outtunnel?.ToString() ?? "[null]"}, replytunnel: {replytunnel?.ToString() ?? "[null]"}" );
                return;
            }

            var ff = NetDb.Inst.GetClosestFloodfill( 
                    ident, 
                    DatabaseLookupSelectFloodfillCountLS + 2 * info.Retries, 
                    IdentUpdateRequestInfo.AlreadyQueried.Select( d => d.Key ).ToHashSet() )
                .Select( r => new 
                    {
                        Id = r,
                        RInfo = NetDb.Inst[r],
                        NetDb.Inst.Statistics[r].Score
                    } )
                .ToList();

#if LOG_ALL_IDENT_LOOKUPS
            StringBuilder foundrouters = new StringBuilder();
            StringBuilder foundrouterskeys = new StringBuilder();

            foreach ( var router in ff )
            {
                foundrouters.AppendFormat( "{0}{1}{2}", ( foundrouters.Length != 0 ? ", " : "" ), 
                    router.Id.Id32Short, 
                    FreenetBase64.Encode( router.Id.Hash ) );
                foundrouterskeys.AppendFormat( "{0}{1}{2}", ( foundrouterskeys.Length != 0 ? ", " : "" ), 
                    router.Id.Id32Short,
                    FreenetBase64.Encode( router.Id.RoutingKey.Hash ) );
            }
            var st = foundrouters.ToString();
            var st2 = foundrouterskeys.ToString();
            Logging.LogDebugData( $"IdentResolver: foundrouters: {st}" );
            Logging.LogDebugData( $"IdentResolver: foundrouterskeys: {st2}" );
#endif

            if ( !ff.Any() )
            {
                Logging.Log( $"IdentResolver failed to find a floodfill router to lookup ({ident}): " );
                return;
            }

            var floodfills = Enumerable.Range( 0, DatabaseLookupSelectFloodfillCountLS )
                        .Select( i => {
                            var one = ff.RandomWeighted( r => r.Score, 20.0 );
                            ff.Remove( one );
                            return one;
                        } )
                        .Where( ff => ff != null )
                        .ToArray();

            foreach( var oneff in floodfills )
            {
                var oneffid = oneff.Id;

                try
                {
                    var msg = new DatabaseLookupMessage(
                                ident,
                                replytunnel.Destination, replytunnel.GatewayTunnelId,
                                DatabaseLookupMessage.LookupTypes.LeaseSet );

                    var garlic = new Garlic(
                                new GarlicClove(
                                    new GarlicCloveDeliveryLocal( msg ) )
                            );

                    var egmsg = Garlic.EGEncryptGarlic( garlic, oneff.RInfo.Identity.PublicKey, new I2PSessionKey(), null );

                    outtunnel.Send( new TunnelMessageRouter( egmsg, oneffid ) );

                    IdentUpdateRequestInfo.AlreadyQueried[oneffid] = 1;

#if LOG_ALL_IDENT_LOOKUPS
                    Logging.Log( $"IdentResolver: LeaseSet query {ident.Id32Short} " +
                        $"sent to {oneffid.Id32Short}. Dist: {oneffid ^ ident.RoutingKey}" );
#endif
                }
                catch ( Exception ex )
                {
                    Logging.Log( "SendLSDatabaseLookup", ex );
                }
            }
            info.StartLookup( floodfills.Select( ff => ff.Id ) );
            }
            catch( Exception ex )
            {
                Logging.Log( "SendLSDatabaseLookup2", ex );
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
            if ( NetDb.Inst.RouterCount > 1000 
                    && ExploreNewRouters.Frequency < TickSpan.Minutes( 1 ) )
            {
                ExploreNewRouters.Frequency = TickSpan.Minutes( 5 );
            }

            I2PIdentHash ident = new I2PIdentHash( true );

            var ff = NetDb.Inst.GetClosestFloodfill( ident, 10, null )
                    .Shuffle()
                    .Take( DatabaseLookupSelectFloodfillCountRI )
                    .ToArray();

            foreach ( var oneff in ff )
            {
                var msg = new DatabaseLookupMessage(
                            ident,
                            RouterContext.Inst.MyRouterIdentity.IdentHash,
                            DatabaseLookupMessage.LookupTypes.Exploration,
                            new I2PIdentHash[] { new I2PIdentHash( false ) } );

#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( $"IdentResolver: Random router lookup {ident.Id32Short} sent to {oneff.Id32Short}" );
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
            var retry = OutstandingQueries.Where( i =>
                    i.Value != null
                    && i.Value.Start.DeltaToNow > i.Value.DatabaseLookupWaitTime )
                .Select( i => i.Value )
                .ToArray();

            foreach( var info in retry )
            {
                // Collect router performance
                var noresponse = info.FloodfillResponses
                        .Where( r => r.Value.Response == ReceivedFloodfillResponses.NoResponse );

                // Give all the credit
                foreach( var one in noresponse )
                {
                    var from = one.Key;
                    
                    if ( info.LookupType == DatabaseLookupMessage.LookupTypes.LeaseSet )
                    {
                        NetDb.Inst.Statistics.IdentResolveLSTimeout( from );
                    }
                    else
                    {
                        NetDb.Inst.Statistics.IdentResolveRITimeout( from );
                    }

                    info.FloodfillResponses[from]
                        = new FloodfillResponse()
                        {
                            Floodfill = from,
                            Response = ReceivedFloodfillResponses.Timeout
                        };
                }
            }

            SendRetries( retry );
        }

        protected void SendRetries( IEnumerable<IdentUpdateRequestInfo> retry )
        {
            foreach ( var one in retry )
            {
                var isleaseset = one.LookupType == DatabaseLookupMessage.LookupTypes.LeaseSet;

                if ( one.Retries >= ( isleaseset ? DatabaseLookupRetriesLS : DatabaseLookupRetriesRI ) )
                {
                    OutstandingQueries.TryRemove( one.LookupIdent, out _ );

                    Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} failed with timeout.",
                        ( isleaseset ? "LeaseSet" : "RouterInfo" ), 
                        one.LookupIdent.Id32Short ) );

                    if ( LookupFailure != null ) ThreadPool.QueueUserWorkItem( a => LookupFailure( one.LookupIdent ) );

                    continue;
                }

                ++one.Retries;
                one.Start.SetNow();

#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( string.Format( "IdentResolver: Lookup of {0} {1} failed with timeout Retry {2}.",
                    ( isleaseset ? "LeaseSet" : "RouterInfo" ), one.LookupIdent.Id32Short, one.Retries ) );
#endif
                if ( isleaseset )
                {
                    SendLSDatabaseLookup( one.LookupIdent, one );
                }
                else
                {
                    SendRIDatabaseLookup( one.LookupIdent, one );
                }
            }
        }
    }
}
