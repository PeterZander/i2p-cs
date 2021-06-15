using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Utils;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.SessionLayer;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TransportLayer;
using I2PCore.Data;
using System.Collections.Concurrent;

namespace I2PCore
{
    public class FloodfillUpdater
    {
        public static readonly TickSpan DatabaseStoreNonReplyTimeout = TickSpan.Seconds( 15 );

        PeriodicAction StartNewUpdateRouterInfo = new PeriodicAction( NetDb.RouterInfoExpiryTime / 5, true );
        PeriodicAction CheckForTimouts = new PeriodicAction( TickSpan.Seconds( 5 ) );

        class FFUpdateRequestInfo
        {
            public readonly TickCounter Start = new TickCounter();
            public readonly I2PIdentHash IdentToUpdate;
            public readonly ILeaseSet LeaseSet;

            public I2PIdentHash CurrentTargetFF { get; set; }

            public TimeWindowDictionary<I2PIdentHash,object> Exclude;

            public TickSpan Timeout => LeaseSet is null
                    ? DatabaseStoreNonReplyTimeout
                    : DatabaseStoreNonReplyTimeout * 2;

            public readonly uint Token;
            public readonly int Retries;

            public FFUpdateRequestInfo( I2PIdentHash ff, uint token, I2PIdentHash id )
            {
                Exclude = new TimeWindowDictionary<I2PIdentHash,object>( DatabaseStoreNonReplyTimeout * 10 );
                CurrentTargetFF = ff;
                Token = token;
                IdentToUpdate = id;
            }

            public FFUpdateRequestInfo( I2PIdentHash ff, uint token, ILeaseSet ls, int retries )
            {
                Exclude = new TimeWindowDictionary<I2PIdentHash,object>( DatabaseStoreNonReplyTimeout * 10 );
                CurrentTargetFF = ff;
                Token = token;
                LeaseSet = ls;
                Retries = retries;
            }

            public override string ToString()
            {
                return $"{(LeaseSet is null ? "RI" : "LS")} {Token,10} {CurrentTargetFF.Id32Short}";
            }
        }

        ConcurrentDictionary<uint, FFUpdateRequestInfo> OutstandingRequests =
                new ConcurrentDictionary<uint, FFUpdateRequestInfo>();

        public FloodfillUpdater()
        {
            InboundTunnel.DeliveryStatusReceived += InboundTunnel_DeliveryStatusReceived;
            Router.DeliveryStatusReceived += InboundTunnel_DeliveryStatusReceived;
        }

        void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage msg )
        {
            if ( !OutstandingRequests.TryRemove( msg.StatusMessageId, out var info ) )
            {
                /*
                Logging.LogDebug( $"FloodfillUpdater: Floodfill delivery status " +
                    $"{msg.StatusMessageId,10} unknown. Dropped." );
                    */                  
                return;
            }

            var id = info?.LeaseSet is null 
                    ? info?.IdentToUpdate 
                    : info.LeaseSet.Destination.IdentHash;

            Logging.LogDebug( $"FloodfillUpdater: Floodfill delivery status {info} " +
                $"received in {info.Start.DeltaToNowMilliseconds} mseconds." );

            NetDb.Inst.Statistics.FloodfillUpdateSuccess( info.CurrentTargetFF );
        }

        public void Run()
        {
            StartNewUpdateRouterInfo.Do( StartNewUpdatesRouterInfo );
            CheckForTimouts.Do( CheckTimeouts );
        }

        public void TrigUpdateRouterInfo( string reason )
        {
            if ( StartNewUpdateRouterInfo.Autotrigger || StartNewUpdateRouterInfo.LastAction.DeltaToNowSeconds > 15 )
            {
                Logging.LogDebug( $"FloodFillUpdater: New update triggered. Reason: {reason}" );
                StartNewUpdateRouterInfo.TimeToAction = TickSpan.Seconds( 5 );
            }
            else
            {
                Logging.LogDebug( $"FloodFillUpdater: New update request ignored. Reason: {reason}" );
            }
        }

        public void TrigUpdateLeaseSet( ILeaseSet leaseset )
        {
            StartNewUpdatesLeaseSet( leaseset );
        }

        void StartNewUpdatesRouterInfo()
        {
            var list = GetNewFFList(
                    RouterContext.Inst.MyRouterIdentity.IdentHash,
                    4,
                    null );

            foreach ( var ff in list )
            {
                try
                {
                    var token = BufUtils.RandomUint() | 1;

                    Logging.Log( string.Format( "FloodfillUpdater: {0}, RI, token: {1,10}, dist: {2}.",
                        ff.Id32Short, token,
                        ff ^ RouterContext.Inst.MyRouterIdentity.IdentHash.RoutingKey ) );

                    OutstandingRequests[token] = new FFUpdateRequestInfo( 
                            ff,
                            token,
                            RouterContext.Inst.MyRouterIdentity.IdentHash );

                    SendUpdate( ff, token );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        void StartNewUpdatesLeaseSet( ILeaseSet ls )
        {
            // old lease sets are out of date
            while ( OutstandingRequests.TryRemove( 
                    OutstandingRequests.Where( r => r.Value?.LeaseSet?.Destination.IdentHash == ls.Destination.IdentHash )
                        .Select( r => r.Key )
                        .FirstOrDefault(),
                    out var _ ) )
            {
            }

            var list = GetNewFFList(
                    ls.Destination.IdentHash,
                    3,
                    null );

            var destinations = list.Select( i => NetDb.Inst[i] );

            foreach ( var ff in destinations )
            {
                try
                {
                    var ffident = ff.Identity.IdentHash;
                    var token = BufUtils.RandomUint() | 1;

                    Logging.Log( $"FloodfillUpdater: New LS update started for " +
                        $"{ls.Destination.IdentHash.Id32Short}, {ls.Leases.Count()} leases " +
                        $"update {ffident.Id32Short}, token {token,10}, " +
                        $"dist: {ffident ^ ls.Destination.IdentHash.RoutingKey}." );

                    OutstandingRequests[token] = new FFUpdateRequestInfo( ffident, token, ls, 0 );

                    SendLeaseSetUpdateGarlic( ffident, ff.Identity.PublicKey, ls, token );
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        private void SendUpdate( I2PIdentHash ff, uint token )
        {
            // If greater than zero, a DeliveryStatusMessage
            // is requested with the Message ID set to the value of the Reply Token.
            // A floodfill router is also expected to flood the data to the closest floodfill peers
            // if the token is greater than zero.
            // https://geti2p.net/spec/i2np#databasestore

            var ds = new DatabaseStoreMessage(
                            RouterContext.Inst.MyRouterInfo,
                            token,
                            RouterContext.Inst.MyRouterInfo.Identity.IdentHash,
                            0 );

            TransportProvider.Send( ff, ds );
        }

        private void SendLeaseSetUpdateGarlic(
                I2PIdentHash ffdest,
                I2PPublicKey pubkey,
                ILeaseSet ls,
                uint token )
        {
            // If greater than zero, a DeliveryStatusMessage
            // is requested with the Message ID set to the value of the Reply Token.
            // A floodfill router is also expected to flood the data to the closest floodfill peers
            // if the token is greater than zero.
            // https://geti2p.net/spec/i2np#databasestore

            var outtunnel = TunnelProvider.Inst.GetEstablishedOutboundTunnel( false );

            var replytunnel = ls.Leases.Random();

            if ( outtunnel is null || replytunnel is null )
            {
                Logging.LogDebug( $"SendLeaseSetUpdateGarlic: " +
                    $"outtunnel: {outtunnel}, replytunnel: {replytunnel?.TunnelGw?.Id32Short}" );
                return;
            }

            var ds = new DatabaseStoreMessage( ls, token, replytunnel.TunnelGw, replytunnel.TunnelId );

            // As explained on the network database page, local LeaseSets are sent to floodfill 
            // routers in a Database Store Message wrapped in a Garlic Message so it is not 
            // visible to the tunnel's outbound gateway.

            var garlic = new Garlic(
                        new GarlicClove(
                            new GarlicCloveDeliveryLocal( ds ) )
                    );

            var egmsg = Garlic.EGEncryptGarlic( garlic, pubkey, new I2PSessionKey(), null );

            outtunnel.Send(
                new TunnelMessageRouter(
                    egmsg,
                    ffdest ) );
        }

        void CheckTimeouts()
        {
            KeyValuePair<uint, FFUpdateRequestInfo>[] timeout;

            timeout = OutstandingRequests
                    .Where( r => r.Value.Start.DeltaToNow > r.Value.Timeout )
                    .ToArray();

            foreach ( var one in timeout )
            {
                Logging.LogDebug( $"FloodfillUpdater: Update {one.Key,10} failed with timeout." );
                NetDb.Inst.Statistics.FloodfillUpdateTimeout( one.Value.CurrentTargetFF );
            }

            TimeoutRegenerateRIUpdate( timeout
                    .Where( t => t.Value?.LeaseSet is null )
                    .Select( t => t.Value ) );

            TimeoutRegenerateLSUpdate( timeout
                    .Where( t => 
                        !( t.Value?.LeaseSet is null ) )
                    .Select( t => t.Value ) );
        }

        private void TimeoutRegenerateRIUpdate( IEnumerable<FFUpdateRequestInfo> rinfos )
        {
            var list = GetNewFFList(
                    RouterContext.Inst.MyRouterIdentity.IdentHash,
                    rinfos.Count(),
                    rinfos.SelectMany( inf => inf.Exclude?.Select( e => e.Key ) ).ToHashSet() );

            foreach ( var rinfo in rinfos )
            {
                OutstandingRequests.TryRemove( rinfo.Token, out _ );
                var ff = list.Random();

                var token = BufUtils.RandomUint() | 1;

                Logging.LogDebug( string.Format( "FloodfillUpdater: RI replacement update {0}, token {1,10}, dist: {2}.",
                    ff.Id32Short, token,
                    ff ^ RouterContext.Inst.MyRouterIdentity.IdentHash.RoutingKey ) );

                SendUpdate( ff, token );

                var newreq = new FFUpdateRequestInfo( 
                        ff, 
                        token,
                        RouterContext.Inst.MyRouterIdentity.IdentHash );
                newreq.Exclude = rinfo.Exclude;
                newreq.Exclude[ff] = 1;

                OutstandingRequests[token] = newreq;

            }
        }

        private void TimeoutRegenerateLSUpdate( IEnumerable<FFUpdateRequestInfo> lsets )
        {
            foreach ( var lsinfo in lsets )
            {
                OutstandingRequests.TryRemove( lsinfo.Token, out _ );

                var token = BufUtils.RandomUint() | 1;

                var ls = lsinfo.LeaseSet;

                var list = NetDb.Inst.GetClosestFloodfill(
                            ls.Destination.IdentHash,
                            2 + 2 * lsinfo.Retries,
                            lsets.SelectMany( inf => inf.Exclude?.Select( e => e.Key ) ).ToHashSet() );

                var ff = list.Random();
                if ( ff is null ) continue;
                
                var ffident = NetDb.Inst[ff];

                Logging.Log( $"FloodfillUpdater: LS {ls.Destination.IdentHash.Id32Short} " +
                    $"replacement update {ff.Id32Short}, token {token,10}, " +
                    $"dist: {ff ^ ls.Destination.IdentHash.RoutingKey}." );

                SendLeaseSetUpdateGarlic( 
                        ffident.Identity.IdentHash,
                        ffident.Identity.PublicKey,
                        ls,
                        token );

                var newreq = new FFUpdateRequestInfo(
                        ff,
                        token,
                        ls,
                        lsinfo.Retries + 1 );

                newreq.Exclude = lsinfo.Exclude;
                newreq.Exclude[ff] = 1;

                OutstandingRequests[token] = newreq;
            }
        }

        private static IEnumerable<I2PIdentHash> GetNewFFList( I2PIdentHash id, int count, ICollection<I2PIdentHash> exclude )
        {
            var list = NetDb.Inst
                .GetClosestFloodfill(
                    id,
                    count * 20,
                    null );

            if ( !( list?.Any() ?? false ) )
            {
                list = NetDb.Inst.GetRandomFloodfillRouter( true, 20 );
            }

            var p = list.Select( i =>
                new
                {
                    Id = i,
                    NetDb.Inst.Statistics[i].Score
                } ).ToList();

            var result = new List<I2PIdentHash>();
            var pmin = p.Min( s => s.Score );

            var i = 0;
            while ( i++ < count * 2 && result.Count < count )
            {
                var r = p.RandomWeighted( wr =>
                    float.IsNaN( wr.Score ) ? 1f : wr.Score - pmin + 1,
                    false,
                    2.0 );

                if ( !result.Any( id => id == r.Id ) )
                {
                    result.Add( r.Id );
                    p.Remove( r );
                }
            }

            return result;
        }
    }
}
