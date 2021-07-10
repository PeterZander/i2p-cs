using System;
using System.Linq;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.Utils;
using System.Collections.Generic;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        internal InboundTunnel SelectInboundTunnel()
        {
            return TunnelProvider.SelectTunnel( InboundEstablishedPool.Keys, 5 );
        }

        internal OutboundTunnel SelectOutboundTunnel()
        {
            return TunnelProvider.SelectTunnel( OutboundEstablishedPool.Keys, 5 );
        }

        public static ILease SelectLease( IEnumerable<ILease> ls )
        {
            var result = ls
                    .Where( l => l.Expire > DateTime.UtcNow + MinLeaseLifetime )
                    .OrderByDescending( l => l.Expire )
                    .Take( 2 );

            if ( !result.Any() )
            {
                result = ls
                    .Where( l => l.Expire > DateTime.UtcNow )
                    .OrderByDescending( l => l.Expire )
                    .Take( 2 );
            }

            return result.Random();
        }

        void RemovePendingTunnel( Tunnel tunnel )
        {
            if ( tunnel is OutboundTunnel ot )
            {
                OutboundPending.TryRemove( ot, out _ );
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundPending.TryRemove( it, out _ );
            }
        }

        void RemovePoolTunnel( Tunnel tunnel, RemovalReason reason )
        {
            if ( tunnel is OutboundTunnel ot )
            {
                OutboundEstablishedPool.TryRemove( ot, out _ );
            }

            if ( tunnel is InboundTunnel it )
            {
                var removed = InboundEstablishedPool.TryRemove( it, out _ );
                RemoveTunnelFromEstablishedLeaseSet( (InboundTunnel)tunnel );

                if ( removed && reason != RemovalReason.Expired )
                {
                    UpdateSignedLeases();
                }
            }
        }

        void InboundTunnel_TunnelShutdown( Tunnel tunnel )
        {
            tunnel.TunnelShutdown -= InboundTunnel_TunnelShutdown;

            if ( tunnel is InboundTunnel )
            {
                ((InboundTunnel)tunnel).GarlicMessageReceived -= InboundTunnel_GarlicMessageReceived;
            }
        }
    }
}