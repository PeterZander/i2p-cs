using System;
namespace I2PCore.TunnelLayer
{
    public interface ITunnelOwner
    {
        void TunnelBuildFailed( Tunnel tunnel, bool timeout );
        void TunnelEstablished( Tunnel tunnel );
        void TunnelFailed( Tunnel tunnel );
        void TunnelExpired( Tunnel tunnel );
    }
}
