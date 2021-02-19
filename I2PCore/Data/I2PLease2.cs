using System;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLease2 : I2PType, ILease
    {
        public const int DefaultLeaseLifetimeSeconds = 10 * 60;

        public I2PIdentHash TunnelGw { get; private set; }
        public I2PTunnelId TunnelId { get; private set; }
        public I2PDateShort EndDate { get; private set; }

        public I2PLease2( I2PIdentHash tunnelgw, I2PTunnelId tunnelid, I2PDateShort enddate )
        {
            TunnelGw = tunnelgw;
            TunnelId = tunnelid;
            EndDate = enddate;
        }

        public I2PLease2( I2PIdentHash tunnelgw, I2PTunnelId tunnelid )
        {
            TunnelGw = tunnelgw;
            TunnelId = tunnelid;
            EndDate = new I2PDateShort( 
                DateTime.UtcNow 
                    + TimeSpan.FromSeconds( DefaultLeaseLifetimeSeconds ) );
        }

        public I2PLease2( BufRef reader )
        {
            TunnelGw = new I2PIdentHash( reader );
            TunnelId = new I2PTunnelId( reader );

            EndDate = new I2PDateShort( reader );
        }

        public void Write( BufRefStream dest )
        {
            TunnelGw.Write( dest );
            TunnelId.Write( dest );
            EndDate.Write( dest );
        }

        // ILease
        public DateTime Expire { get => (DateTime)EndDate; }

        public override string ToString()
        {
            return $"I2PLease2 GW {TunnelGw.Id32Short}, Id {TunnelId}, Exp {EndDate}";
        }
    }
}
