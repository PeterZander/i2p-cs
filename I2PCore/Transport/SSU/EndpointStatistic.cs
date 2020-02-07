using System;
using System.Net;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class EndpointStatistic
    {
        const double SuccessPenalty = -5.0;
        const double TimoutPenalty = 5000.0;

        public readonly TickCounter Created = TickCounter.Now;
        private readonly EndPoint Endpoint;

        public EndpointStatistic( EndPoint ep )
        {
            Endpoint = ep;
        }

        public TickSpan MinConnectionTime { get; internal set; } = TickSpan.Minutes( 3 );
        public TickSpan SessionLengths { get; internal set; } = TickSpan.Seconds( 5 );
        public int ConnectionSuccess { get; internal set; }
        public int ConnectionTimeouts { get; internal set; }

        public double Score
        {
            get
            {
                return MinConnectionTime.ToMilliseconds
                    - SessionLengths.ToMinutes * 1000.0
                    + ConnectionSuccess * SuccessPenalty
                    + ConnectionTimeouts * TimoutPenalty;
            }
        }

        public override string ToString()
        {
            return $"EndpointStatistic: ({Score,10:#0.0}) {Endpoint,-25}, " +
                $"MinConnectionTime: {MinConnectionTime,-20}, " +
                $"SessionLengths: {SessionLengths,-23}, " +
                $"ConnectionTimeouts: {ConnectionTimeouts,4}, " +
                $"ConnectionSuccess: {ConnectionSuccess,4}";
        }
    }
}
