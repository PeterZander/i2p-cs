using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    public class EndpointStatistics
    {
        readonly Dictionary<EndPoint, EndpointStatistic>
            Database = new Dictionary<EndPoint, EndpointStatistic>();

        public EndpointStatistics()
        {
        }

        public EndpointStatistic this[EndPoint ep]
        {
            get
            {
                lock ( Database )
                {
                    if ( !Database.TryGetValue( ep, out var es ) )
                        return new EndpointStatistic( ep );

                    return es;
                }
            }
        }

        protected void Update( EndPoint ep, Action<EndpointStatistic> action )
        {

            lock ( Database )
            {
                if ( !Database.TryGetValue( ep, out var es ) )
                {
                    es = new EndpointStatistic( ep );
                    Database[ep] = es;
                }

                action( es );
            }
        }
        public void ConnectionTimeout( IPEndPoint ep )
        {
            Update( ep, es => ++es.ConnectionTimeouts );
        }

        public void ConnectionSuccess( IPEndPoint ep )
        {
            Update( ep, es => ++es.ConnectionSuccess );
        }

        public void UpdateConnectionTime( EndPoint ep, TickSpan time )
        {
            Update( ep, es =>
            {
                if ( time < es?.MinConnectionTime )
                    es.MinConnectionTime = time;
            } );
        }

        internal void UpdateSessionLength( IPEndPoint ep, TickSpan time )
        {
            Update( ep, es =>
            {
                es.SessionLengths = TickSpan.Seconds(
                    (int)( ( 9.0 * es.SessionLengths.ToSeconds + time.ToSeconds )
                    / 10 ) );
            } );
        }

        public IEnumerable<KeyValuePair<EndPoint,EndpointStatistic>> ToArray()
        {
            lock ( Database )
            {
                return Database.ToArray();
            }
        }
    }
}
