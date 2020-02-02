using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using I2PCore.Utils;

namespace I2PCore.Transport
{
    internal class DecayingIPBlockFilter
    {
        const int BlockTimeMinutes = 3 * 60;
        const int IPFaultHistoryWindowMinutes = 30;
        const int NumberOfFailuresToBlock = 8;

        Dictionary<IPAddress, LinkedList<TickCounter>> MonitorIPWindow = new Dictionary<IPAddress, LinkedList<TickCounter>>();
        Dictionary<IPAddress, TickCounter> BlockedIPs = new Dictionary<IPAddress, TickCounter>();

        PeriodicAction Decay = new PeriodicAction( TickSpan.Seconds( ( IPFaultHistoryWindowMinutes * 60 ) / NumberOfFailuresToBlock ) );

        internal int Count { get { return BlockedIPs.Count; } }

        internal void ReportProblem( IPAddress addr )
        {
            LinkedList<TickCounter> list;

            lock ( MonitorIPWindow )
            {
                if ( MonitorIPWindow.TryGetValue( addr, out list ) )
                {
                    list.AddFirst( TickCounter.Now );
                }
                else
                {
                    list = new LinkedList<TickCounter>();
                    list.AddFirst( TickCounter.Now );
                    MonitorIPWindow[addr] = list;
                }
            }

            if ( list.Count == NumberOfFailuresToBlock )
            {
                Logging.LogTransport( "DecayingIPBlockFilter: Blocking " + addr.ToString() );

                lock ( BlockedIPs )
                {
                    BlockedIPs[addr] = TickCounter.Now;
                }
            }
        }

        internal bool IsFiltered( IPAddress addr )
        {
            Decay.Do( () =>
            {
                var toremove = new List<IPAddress>();

                lock ( MonitorIPWindow )
                {
                    foreach ( var one in MonitorIPWindow )
                    {
                        var startcount = one.Value.Count;

                        while ( one.Value.Count > 0 && one.Value.Last.Value.DeltaToNow.ToMinutes > IPFaultHistoryWindowMinutes )
                            one.Value.RemoveLast();

                        if ( startcount >= NumberOfFailuresToBlock && one.Value.Count < NumberOfFailuresToBlock )
                        {
                            Logging.LogTransport( "DecayingIPBlockFilter: Window under blocking level " + one.Key.ToString() );
                            toremove.Add( one.Key );
                        }
                        else
                        if ( one.Value.Count == 0 )
                        {
                            toremove.Add( one.Key );
                        }
                    }

                    foreach ( var remove in toremove )
                    {
                        MonitorIPWindow.Remove( remove );
                    }
                }
            } );

            lock ( BlockedIPs )
            {
                TickCounter blocktime;

                if ( BlockedIPs.TryGetValue( addr, out blocktime ) )
                {
                    if ( blocktime.DeltaToNow.ToMinutes > BlockTimeMinutes )
                    {
                        Logging.LogTransport( "DecayingIPBlockFilter: Unblocking " + addr.ToString() );
                        BlockedIPs.Remove( addr );
                        return false;
                    }

                    return true;
                }

                return false;
            }
        }
    }
}
