using System;
using I2PCore.TunnelLayer;

namespace I2PCore.SessionLayer
{
    internal interface IClient
    {
        int InboundTunnelHopCount { get; }
        int OutboundTunnelHopCount { get; }

        int TargetInboundTunnelCount { get; }
        int TargetOutboundTunnelCount { get; }

        int InboundTunnelsNeeded { get; }
        int OutboundTunnelsNeeded { get; }

        bool ClientTunnelsStatusOk { get; }

        void AddOutboundPending( OutboundTunnel tunnel );
        void AddInboundPending( InboundTunnel tunnel );
        void TunnelEstablished( Tunnel tunnel );
        void RemoveTunnel( Tunnel tunnel );

        void Execute();
    }
}
