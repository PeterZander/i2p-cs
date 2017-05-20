using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class BandwidthLimiter
    {
        Bandwidth BandwidthMeasurement;
        float MaxKbPS;

        const float StartDiscardingLimitFactor = 0.9f;
        float StartDiscardingLimit;
        float ProbabilityWindow;

        public BandwidthLimiter( Bandwidth bwref, float maxkbps )
        {
            BandwidthMeasurement = bwref;

            MaxKbPS = maxkbps;
            StartDiscardingLimit = MaxKbPS * StartDiscardingLimitFactor;
            ProbabilityWindow = MaxKbPS - StartDiscardingLimit;
        }

        Random Rnd = new Random();

        public bool DropMessage()
        {
            var br = BandwidthMeasurement.Bitrate;
            if ( MaxKbPS == 0f || br < StartDiscardingLimit ) return false;

            var probability = ( br - StartDiscardingLimit ) / ProbabilityWindow;
            return Rnd.NextDouble() < probability;
        }
    }
}
