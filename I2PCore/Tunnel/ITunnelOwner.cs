using System;
namespace I2PCore.Tunnel
{
    public interface ITunnelOwner
    {
        void TunnelBuildTimeout( Tunnel tunnel );
        void TunnelEstablished( Tunnel tunnel );
        void TunnelFailed( Tunnel tunnel );
        void TunnelExpired( Tunnel tunnel );
    }
}
