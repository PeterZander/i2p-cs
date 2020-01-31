#define LOG_ALL_IDENT_LOOKUPS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Transport;
using I2PCore.Data;
using System.Threading;

namespace I2PCore
{
    public class IdentResolver
    {
        public readonly TickSpan DatabaseLookupWaitTime = TickSpan.Seconds( 10 );
        public const int DatabaseLookupRetries = 3;
        public const int DatabaseLookupSelectFloodfillCount = 2;

        PeriodicAction CheckForTimouts = new PeriodicAction( TickSpan.Seconds( 15 ) );
        PeriodicAction ExploreNewRouters = new PeriodicAction( TickSpan.Seconds( 20 ) );

        public delegate void IdentResolverResultFail( I2PIdentHash key );
        public delegate void IdentResolverResultRouterInfo( I2PRouterInfo ri );
        public delegate void IdentResolverResultLeaseSet( I2PLeaseSet ls );

        public event IdentResolverResultFail LookupFailure;
        public event IdentResolverResultRouterInfo RouterInfoReceived;
        public event IdentResolverResultLeaseSet LeaseSetReceived;

        class IdentUpdateRequestInfo
        {
            public readonly TickCounter Start = new TickCounter();
            public readonly I2PIdentHash IdentKey;
            public readonly DatabaseLookupMessage.LookupTypes LookupType;

            public int WaitingFor;
            public int Retries;
            public TickCounter LastQuery = TickCounter.MaxDelta;

            public List<I2PIdentHash> AlreadyQueried = new List<I2PIdentHash>();

            public IdentUpdateRequestInfo( I2PIdentHash id, DatabaseLookupMessage.LookupTypes lookuptype )
            {
                IdentKey = id;
                LookupType = lookuptype;
                Retries = 0;
                WaitingFor = DatabaseLookupSelectFloodfillCount;
            }
        }

        Dictionary<I2PIdentHash, IdentUpdateRequestInfo> OutstandingQueries = new Dictionary<I2PIdentHash, IdentUpdateRequestInfo>();

        public IdentResolver( NetDb db )
        {
            db.RouterInfoUpdates += new NetDb.NetworkDatabaseRouterInfoUpdated( NetDb_RouterInfoUpdates );
            db.LeaseSetUpdates += new NetDb.NetworkDatabaseLeaseSetUpdated( NetDb_LeaseSetUpdates );
            db.DatabaseSearchReplies += new NetDb.NetworkDatabaseDatabaseSearchReplyReceived( NetDb_DatabaseSearchReplies );
        }

        public void LookupRouterInfo( I2PIdentHash ident )
        {
            var newitem = new IdentUpdateRequestInfo( ident, DatabaseLookupMessage.LookupTypes.RouterInfo );
            lock ( OutstandingQueries )
            {
                Logging.Log( string.Format( "IdentResolver: Starting lookup of RouterInfo for {0}.", ident.Id32Short ) );
                OutstandingQueries[ident] = newitem;
            }
            SendRIDatabaseLookup( ident, newitem );
        }

        public void LookupLeaseSet( I2PIdentHash ident )
        {
            var newitem = new IdentUpdateRequestInfo( ident, DatabaseLookupMessage.LookupTypes.LeaseSet );
            lock ( OutstandingQueries )
            {
                Logging.Log( string.Format( "IdentResolver: Starting lookup of LeaseSet for {0}.", ident.Id32Short ) );
                OutstandingQueries[ident] = newitem;
            }
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
                //foundrouters.AppendFormat( "{0}{1}{2}", ( foundrouters.Length != 0 ? ", " : "" ), router.Id32Short, FreenetBase64.Encode( router.Hash ) );
#endif
                if ( NetDb.Inst.Contains( router ) )
                {
#if LOG_ALL_IDENT_LOOKUPS
                    Logging.Log( string.Format( "IdentResolver: Not looking up RouterInfo {0} from SearchReply as its already known.", router.Id32Short ) );
#endif
                    continue;
                }
                LookupRouterInfo( router );
            }

            lock ( OutstandingQueries )
            {
                IdentUpdateRequestInfo info;

                if ( !OutstandingQueries.TryGetValue( dsm.Key, out info ) ) return;

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

                    OutstandingQueries.Remove( dsm.Key );
                    if ( LookupFailure != null ) ThreadPool.QueueUserWorkItem( a => LookupFailure( dsm.Key ) );
                }
            }
        }

        void NetDb_LeaseSetUpdates( I2PLeaseSet ls )
        {
            IdentUpdateRequestInfo info;

            lock ( OutstandingQueries )
            {
                if ( !OutstandingQueries.TryGetValue( ls.Destination.IdentHash, out info ) ) return;
                OutstandingQueries.Remove( ls.Destination.IdentHash );
            }

            Logging.Log( string.Format( "IdentResolver: Lookup of LeaseSet {0} succeeded. {1}", 
                ls.Destination.IdentHash.Id32Short, info.Start.DeltaToNow ) );
            if ( LeaseSetReceived != null ) ThreadPool.QueueUserWorkItem( a => LeaseSetReceived( ls ) );
        }

        void NetDb_RouterInfoUpdates( I2PRouterInfo ri )
        {
            IdentUpdateRequestInfo info;

            lock ( OutstandingQueries )
            {
                if ( !OutstandingQueries.TryGetValue( ri.Identity.IdentHash, out info ) ) return;
                OutstandingQueries.Remove( ri.Identity.IdentHash );
            }

            Logging.Log( string.Format( "IdentResolver: Lookup of RouterInfo {0} succeeded. {1}", 
                ri.Identity.IdentHash.Id32Short, info.Start.DeltaToNow ) );
            if ( RouterInfoReceived != null ) ThreadPool.QueueUserWorkItem( a => RouterInfoReceived( ri ) );
        }

        public void Run()
        {
            CheckForTimouts.Do( () => CheckTimeouts() );
            ExploreNewRouters.Do( () => ExplorationRouterLookup() );
        }

        private void SendRIDatabaseLookup( I2PIdentHash ident, IdentUpdateRequestInfo info )
        {
            var ff = NetDb.Inst.GetClosestFloodfill( ident, 10, info.AlreadyQueried, false ).ToArray();
            if ( ff == null || ff.Length == 0 )
            {
                Logging.Log( "IdentResolver: failed to find a floodfill router to lookup (" + ident.ToString() + "): " );
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
                    Logging.Log( "IdentResolver: RouterInfo query " + msg.Key.Id32Short + " sent to " + oneff.Id32Short );
#endif
                }
                catch ( Exception ex )
                {
                    Logging.Log( "SendRIDatabaseLookup", ex );
                }
            }

            lock ( info.AlreadyQueried )
            {
                info.AlreadyQueried.AddRange( ff );
            }
        }

        private void SendLSDatabaseLookup( I2PIdentHash ident, IdentUpdateRequestInfo info )
        {
            /*
            var replytunnel = TunnelProvider.Inst.GetInboundTunnel();
            if ( replytunnel == null ) return;
             */

            var ff = NetDb.Inst.GetClosestFloodfill( ident, DatabaseLookupSelectFloodfillCount * 2, info.AlreadyQueried, false ).ToArray();

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

            if ( ff == null || ff.Length == 0 )
            {
                Logging.Log( "IdentResolver failed to find a floodfill router to lookup (" + ident.ToString() + "): " );
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
                            DatabaseLookupMessage.LookupTypes.LeaseSet );
                /*
                var msg = new DatabaseLookupMessage(
                            ident,
                            replytunnel.Destination, replytunnel.GatewayTunnelId,
                            DatabaseLookupMessage.LookupTypes.LeaseSet, null );
                 */

                //TunnelProvider.Inst.SendEncrypted( oneff.Identity, false, msg );
                TransportProvider.Send( oneff, msg );
#if LOG_ALL_IDENT_LOOKUPS
                Logging.Log( string.Format( "IdentResolver: LeaseSet query {0} sent to {1}. Dist: {2}",
                    msg.Key.Id32Short,
                    oneff.Id32Short,
                    oneff ^ msg.Key.RoutingKey ) );
#endif
                }
                catch ( Exception ex )
                {
                    Logging.Log( "SendLSDatabaseLookup", ex );
                }
            }

            lock ( info.AlreadyQueried )
            {
                info.AlreadyQueried.AddRange( ff );
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

            var ff = NetDb.Inst.GetClosestFloodfill( ident, 10, null, false ).Shuffle().Take( DatabaseLookupSelectFloodfillCount ).ToArray();

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
            IdentUpdateRequestInfo[] timeout;
            IdentUpdateRequestInfo[] retry;

            lock ( OutstandingQueries )
            {
                timeout = OutstandingQueries.Where( i => i.Value.Start.DeltaToNow > DatabaseLookupWaitTime &&
                    i.Value.Retries >= DatabaseLookupRetries ).Select( i => i.Value ).ToArray();
                foreach ( var item in timeout ) OutstandingQueries.Remove( item.IdentKey );

                retry = OutstandingQueries.Where( i => i.Value.Start.DeltaToNow > DatabaseLookupWaitTime ).Select( i => i.Value ).ToArray();
            }

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
