using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Utils;

namespace I2PCore.TunnelLayer
{
    public class TunnelIdSubsriptions
    {
        ConcurrentDictionary<uint, HashSet<Tunnel>> TunnelIds 
            = new ConcurrentDictionary<uint, HashSet<Tunnel>>();

        public void Add( uint id, Tunnel tunnel )
        {
            Logging.LogDebug( $"TunnelIdSubsriptions: Added {id} to {tunnel}" );
            
            var tunnels = TunnelIds.GetOrAdd( id, new HashSet<Tunnel>() );
            tunnels.Add( tunnel );
        }

        public Tunnel Remove( uint id, Tunnel tunnel )
        {
            if ( !TunnelIds.TryGetValue( id, out var tunnels ) ) return null;

            tunnels.Remove( tunnel );
            if ( !tunnels.Any() ) TunnelIds.TryRemove( id, out _ );

            return tunnel;
        }

        public IEnumerable<Tunnel> FindTunnelFromTunnelId( uint tunnelid )
        {
            if ( TunnelIds.TryGetValue( tunnelid, out var tunnels ) )
                return tunnels.ToArray();

            return Enumerable.Empty<Tunnel>();
        }
    }
}
