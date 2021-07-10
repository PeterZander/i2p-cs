using System.Linq;
using I2PCore.TunnelLayer;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        bool IClient.ClientTunnelsStatusOk
        {
            get
            {
                return InboundEstablishedPool.Count >= TargetInboundTunnelCount
                    && OutboundEstablishedPool.Count >= TargetOutboundTunnelCount;
            }
        }

        int IClient.InboundTunnelsNeeded
        {
            get
            {
                var stable = InboundEstablishedPool.Count( t => !t.Key.NeedsRecreation );

                // Even if no individual tunnel is old, the lease set might be
                var lsexpire = InboundEstablishedPool.Max( t => t.Key.CreationTime.DeltaToNow );
                var extra = ( lsexpire?.ToMinutes ?? 0 ) < 4 ? 1 : 0;

                var result = TargetInboundTunnelCount
                            + extra
                            - stable
                            - InboundPending.Count;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"{this}: TargetInboundTunnelCount: {TargetInboundTunnelCount} " +
                            $"Stable: {stable} Pending: {InboundPending.Count} Result: {result}" );
#endif

                return result;
            }
        }

        int IClient.OutboundTunnelsNeeded
        {
            get
            {
                var stable = OutboundEstablishedPool.Count( t => !t.Key.NeedsRecreation );

                // Even if no individual tunnel is old, the lease set might be
                var lsexpire = OutboundEstablishedPool.Max( t => t.Key.CreationTime.DeltaToNow );
                var extra = ( lsexpire?.ToMinutes ?? 0 ) < 4 ? 1 : 0;

                var result = TargetOutboundTunnelCount
                            + extra
                            - stable
                            - OutboundPending.Count;

#if LOG_ALL_TUNNEL_TRANSFER
                Logging.LogDebug( $"{this}: TargetOutboundTunnelCount: {TargetOutboundTunnelCount} " +
                            $"Stable: {stable} Pending: {OutboundPending.Count} Result: {result}" );
#endif

                return result;
            }
        }
        void IClient.AddOutboundPending( OutboundTunnel tunnel )
        {
            OutboundPending[tunnel] = 0;
        }

        void IClient.AddInboundPending( InboundTunnel tunnel )
        {
            InboundPending[tunnel] = 0;
        }

        void IClient.TunnelEstablished( Tunnel tunnel )
        {
            RemovePendingTunnel( tunnel );

            if ( tunnel is OutboundTunnel ot )
            {
                OutboundEstablishedPool[ot] = 0;
            }

            if ( tunnel is InboundTunnel it )
            {
                InboundEstablishedPool[it] = 0;

                it.TunnelShutdown += InboundTunnel_TunnelShutdown;
                it.GarlicMessageReceived += InboundTunnel_GarlicMessageReceived;

                AddTunnelToEstablishedLeaseSet( it );
            }

            UpdateClientState();
        }

        void IClient.RemoveTunnel( Tunnel tunnel, RemovalReason reason )
        {
            RemovePendingTunnel( tunnel );
            RemovePoolTunnel( tunnel, reason );

            UpdateClientState();
        }

        void IClient.Execute()
        {
            if ( Terminated ) return;

            if ( AutomaticIdleUpdateRemotes )
            {
                KeepClientStateUpdated.Do( () =>
                {
                    UpdateClientState();
                } );
            }

            QueueStatusLog.Do( () =>
            {
                Logging.LogInformation(
                    $"{this}: Established tunnels in: {InboundEstablishedPool.Count,2}, " +
                    $"out: {OutboundEstablishedPool.Count,2}. " +
                    $"Pending in: {InboundPending.Count,2}, out {OutboundPending.Count,2}" );
            } );
        }

    }
}