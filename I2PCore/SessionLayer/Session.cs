using System;
using System.Linq;
using I2PCore.Data;
using I2PCore.Utils;
using System.Collections.Generic;
using System.Collections.Concurrent;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;

namespace I2PCore.SessionLayer
{
    internal class Session
    {
        public static TimeSpan RemoteLeaseSetUpdateMargin = TimeSpan.FromMinutes( 3 );

        public static TickSpan SessionInactivityTimeout = TickSpan.Minutes( 25 );

        public static TickSpan WaitForLSUpdateACK = TickSpan.Seconds( 45 );

        readonly internal ClientDestination Context;

        readonly I2PDestination MyDestination;
        readonly I2PIdentHash RemoteDestination;

        readonly EGAESSessionKeyOrigin EGAESKeys;

        TimeWindowDictionary<uint,LeaseSetUpdateACK> NotAckedLSUpdates =
            new TimeWindowDictionary<uint,LeaseSetUpdateACK>( WaitForLSUpdateACK );


        /// <summary>
        /// Expiry time of the newest ACKed LeaseSet transferred to RemoteDestination
        /// </summary>
        protected DateTime ACKedLeaseSetExpireTime = DateTime.MinValue;

        readonly TimeSpan TimeCompareEpsilon = TimeSpan.FromSeconds( 2 );

        public ILeaseSet RemoteLeaseSet { get; protected set; }

        TickCounter LastSendToRemote = new TickCounter();

        internal Session( ClientDestination context, I2PDestination mydest, I2PIdentHash remotedest )
        {
            Context = context;
            MyDestination = mydest;
            RemoteDestination = remotedest;

            EGAESKeys = new EGAESSessionKeyOrigin(
                                    Context,
                                    mydest,
                                    remotedest );
        }

        internal GarlicMessage Encrypt(
            IEnumerable<I2PPublicKey> remotepublickeys,
            InboundTunnel replytunnel,
            IList<GarlicClove> cloves )
        {
            if ( RemoteNeedsLeaseSetUpdate )
            {
                Logging.LogDebug( $"{this}: Sending my leases to remote {RemoteDestination.Id32Short}." );

                GenerateRemoteLSUpdate( cloves, replytunnel );
            }

            return EGAESKeys.Encrypt( remotepublickeys, replytunnel, cloves );
        }

        internal void MySignedLeasesUpdated( I2PIdentHash dest )
        {
            // Send ASAP
            ACKedLeaseSetExpireTime = DateTime.MinValue;

            if ( LastSendToRemote.DeltaToNow < SessionInactivityTimeout )
            {
                SendLeaseSetUpdate( dest );
            }
        }

        public void LeaseSetReceived( ILeaseSet ls )
        {
            lock ( OutboundRemoteLeasePairs )
            {
                if ( RemoteLeaseSet != null 
                        && ls.Expire < RemoteLeaseSet.Expire + TimeCompareEpsilon )
                {
                    Logging.LogDebug(
                        $"{this} Session: LeaseSetReceived: ignoring older remote LS {ls}" );

                    return;
                }

                Logging.LogDebug(
                    $"{this} Session: LeaseSetReceived: updating remote LS {ls}" );

                RemoteLeaseSet = ls;

                // Did a remote pair dissappear?
                var nolongeravailable = OutboundRemoteLeasePairs
                            .Where( p => !ls.Leases.Any( l => l.TunnelId == p.Value.TunnelId
                                                                && l.TunnelGw == p.Value.TunnelGw ) )
                            .ToArray();

                foreach( var toremove in nolongeravailable )
                {
                    OutboundRemoteLeasePairs.TryRemove( toremove.Key, out var _ );
                }
            }
        }

        internal void DeliveryStatusReceived( DeliveryStatusMessage msg, InboundTunnel from )
        {
            EGAESKeys.DeliveryStatusReceived( msg, from );

            if ( NotAckedLSUpdates.TryRemove( msg.StatusMessageId, out var lsupdate ) )
            {
                Logging.LogDebug( $"{this}: Remote LS update ACKed, expire {lsupdate.ExpireTimeForLeaseSet}" );

                RemoteLeaseSetUpdateACKReceived( lsupdate.ExpireTimeForLeaseSet );
            }
        }

        internal void SendLeaseSetUpdate( I2PIdentHash dest )
        {
            if ( RemoteLeaseSet is null )
                return;

            Logging.LogDebug(
                $"{this} Session: SendLeaseSetUpdate: sending LS to {dest.Id32Short}" );

            var replytunnel = Context.SelectInboundTunnel();
            var cloves = GenerateRemoteLSUpdate( new List<GarlicClove>(), replytunnel );

            Context.Send( 
                    RemoteLeaseSet.Destination,
                    Encrypt( 
                            RemoteLeaseSet.PublicKeys,
                            replytunnel,
                            cloves ) );
        }

        internal void DataSentToRemote( I2PIdentHash dest )
        {
            LastSendToRemote.SetNow();
        }

        internal void RemoteIsActive( I2PIdentHash dest )
        {
            if ( RemoteNeedsLeaseSetUpdate
                    && LastSendToRemote.DeltaToNow < SessionInactivityTimeout )
            {
                SendLeaseSetUpdate( dest );
            }
        }

        internal bool RemoteNeedsLeaseSetUpdate
        {
            get => Context.SignedLeases.Expire > ACKedLeaseSetExpireTime + TimeCompareEpsilon;
        }

        internal void RemoteLeaseSetUpdateACKReceived( DateTime expiration )
        {
            if ( expiration > ACKedLeaseSetExpireTime )
            {
                ACKedLeaseSetExpireTime = expiration;
            }
        }

        TimeWindowDictionary<OutboundTunnel,ILease> OutboundRemoteLeasePairs = 
                new TimeWindowDictionary<OutboundTunnel,ILease>( TickSpan.Minutes( 15 ) );

        internal ILease GetTunnelPair( OutboundTunnel outtunnel )
        {
            if ( RemoteLeaseSet is null )
                    return null;

            if ( OutboundRemoteLeasePairs.TryGetValue( outtunnel, out var lease ) )
            {
                if ( lease.Expire > DateTime.UtcNow + TimeSpan.FromMinutes( 1 ) )
                {
                    return lease;
                }

                OutboundRemoteLeasePairs.TryRemove( outtunnel, out var _ );
            }

            var usedleases = OutboundRemoteLeasePairs
                    .Select( p => p.Value )
                    .ToHashSet();

            var unused = RemoteLeaseSet
                            .Leases
                            .Where( lease => !usedleases.Contains( lease ) )
                            .ToArray();
            
            ILease result;

            if ( unused.Length > 0 )
            {
                result = ClientDestination.SelectLease( unused );
            }
            else
            {
                result = ClientDestination.SelectLease( RemoteLeaseSet.Leases );
            }

            if ( result is null ) return null;

            OutboundRemoteLeasePairs[outtunnel] = result;

            return result;
        }

        class LeaseSetUpdateACK
        {
            /// <summary>MessageId of the DeliveryStatusMessage of the ACK.</summary>
            public uint MessageId;
            public DateTime ExpireTimeForLeaseSet;
        }

         IList<GarlicClove> GenerateRemoteLSUpdate( IList<GarlicClove> cloves, InboundTunnel replytunnel )
        {
            var signedleases = Context.SignedLeases;
            var myleases = new DatabaseStoreMessage( signedleases );
            var lsack = new DeliveryStatusMessage( I2NPMessage.GenerateMessageId() );

            cloves.Add(
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        myleases,
                        RemoteDestination ) ) );

            cloves.Add(
                new GarlicClove(
                    new GarlicCloveDeliveryTunnel(
                            lsack,
                            replytunnel.Destination, replytunnel.GatewayTunnelId ) ) );


            NotAckedLSUpdates[lsack.StatusMessageId] = new LeaseSetUpdateACK
            {
                ExpireTimeForLeaseSet = signedleases.Expire,
                MessageId = lsack.MessageId,
            };

            return cloves;
        }

        public override string ToString()
        {
            return $"{Context} {MyDestination} -> {RemoteDestination?.Id32Short}";
        }
   }
}