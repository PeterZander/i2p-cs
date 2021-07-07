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

        readonly internal SessionManager Owner;

        readonly I2PDestination MyDestination;
        readonly I2PIdentHash RemoteDestination;

        readonly EGAESSessionKeyOrigin EGAESKeys;

        /// <summary>
        /// Expiry time of the newest ACKed LeaseSet transferred to RemoteDestination
        /// </summary>
        internal DateTime ACKedLeaseSetExpireTime = DateTime.MinValue;

        public ILeaseSet RemoteLeaseSet { get; protected set; }

        TickCounter LastSendOrReceive = new TickCounter();

        internal Session( SessionManager owner, I2PDestination mydest, I2PIdentHash remotedest )
        {
            Owner = owner;
            MyDestination = mydest;
            RemoteDestination = remotedest;

            EGAESKeys = new EGAESSessionKeyOrigin(
                                    this,
                                    mydest,
                                    remotedest );
        }

        internal GarlicMessage Encrypt(
            IEnumerable<I2PPublicKey> remotepublickeys,
            ILeaseSet publishedleases,
            InboundTunnel replytunnel,
            params GarlicClove[] cloves )
        {
            LastSendOrReceive.SetNow();
            return EGAESKeys.Encrypt( remotepublickeys, publishedleases, replytunnel, cloves );
        }

        internal void MyPublishedLeasesUpdated( I2PIdentHash dest )
        {
            // Send ASAP
            ACKedLeaseSetExpireTime = DateTime.MinValue;

            if ( LastSendOrReceive.DeltaToNow < SessionInactivityTimeout )
            {
                SendLeaseSetUpdate( dest );
            }
        }

        public void LeaseSetReceived( ILeaseSet ls )
        {
            if ( RemoteLeaseSet != null && ls.Expire < RemoteLeaseSet.Expire )
            {
                Logging.LogDebug(
                    $"{Owner} Session: LeaseSetReceived: discarding older LS {ls.Destination}" );

                return;
            }

            Logging.LogDebug(
                $"{Owner} Session: LeaseSetReceived: updating LS for {ls.Destination}" );

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

        internal void SendLeaseSetUpdate( I2PIdentHash dest )
        {
            if ( RemoteLeaseSet != null )
            {
                Owner.Owner.Send( 
                        RemoteLeaseSet.Destination,
                        Encrypt( 
                                RemoteLeaseSet.PublicKeys,
                                Owner.Owner.SignedLeases,
                                Owner.Owner.SelectInboundTunnel() ) );
            }
        }

        internal void RemoteIsActive( I2PIdentHash dest )
        {
            if ( DateTime.UtcNow > ACKedLeaseSetExpireTime + RemoteLeaseSetUpdateMargin
                    && LastSendOrReceive.DeltaToNow < SessionInactivityTimeout )
            {
                SendLeaseSetUpdate( dest );
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
    }
}