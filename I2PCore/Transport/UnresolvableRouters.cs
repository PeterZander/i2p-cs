using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.Transport
{
    internal class UnresolvableRouters
    {
        const int UnresolvableDecayMinutes = 60 * 2;

        Dictionary<I2PIdentHash, TickCounter> CurrentlyUnresolvableRouters = new Dictionary<I2PIdentHash, TickCounter>();

        internal void Add( I2PIdentHash dest )
        {
            Logging.Log( "UnresolvableAddresses: " + dest.Id32Short + " marked as unresolvable." );
            lock ( CurrentlyUnresolvableRouters )
            {
                CurrentlyUnresolvableRouters[dest] = TickCounter.Now;
            }
        }

        internal bool Contains( I2PIdentHash dest )
        {
            lock ( CurrentlyUnresolvableRouters )
            {
                var remove = CurrentlyUnresolvableRouters.Where( d => d.Value.DeltaToNow.ToMinutes > UnresolvableDecayMinutes ).Select( m => m.Key ).ToArray();
                foreach ( var one in remove ) CurrentlyUnresolvableRouters.Remove( one );

                return CurrentlyUnresolvableRouters.ContainsKey( dest );
            }
        }

        internal int Count { get { return CurrentlyUnresolvableRouters.Count; } }
    }
}
