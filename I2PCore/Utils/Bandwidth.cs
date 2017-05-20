using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class Bandwidth
    {
        const int AggregationWindowMilliseconds = 20 * 1000;
        TickCounter AggregationWindowStart = new TickCounter();
        long AggregationWindowDataBytes;
        float AggregatedBitrate = 0f;

        readonly static float EMAAlphaMax = 0.6f;
        float EMAAlpha;
        float EMAAlphaRes;

        Bandwidth Pool;

        public long DataBytes { get; private set; }

        /// <summary>
        /// Bitrate in bit / second
        /// </summary>
        public float Bitrate
        {
            get
            {
                var result = ( AggregatedBitrate * EMAAlpha ) + AggregationWindowBitrate() * EMAAlphaRes;
                BitrateMax = Math.Max( BitrateMax, result );
                return result;
            }
        }

        public float BitrateMax { get; private set; }

        public Bandwidth()
        {
            UpdateAlpha( 0.2f );
        }

        public Bandwidth( Bandwidth pool )
        {
            Pool = pool;
            UpdateAlpha( 0.2f );
        }

        private void UpdateAlpha( float v )
        {
            EMAAlpha = v;
            EMAAlphaRes = 1f - v;
        }

        public void Measure( int size )
        {
            DataBytes += (long)size;
            AggregationWindowDataBytes += (long)size;
            if ( Pool != null ) Pool.Measure( size );

            if ( AggregationWindowStart.DeltaToNowMilliseconds > AggregationWindowMilliseconds )
            {
                UpdateAggregatedBitrate();
            }
        }

        private void UpdateAggregatedBitrate()
        {
            var windowbitrate = AggregationWindowBitrate();
            AggregatedBitrate = ( AggregatedBitrate * EMAAlpha ) + windowbitrate * EMAAlphaRes;

            AggregationWindowDataBytes /= 2;
            AggregationWindowStart = TickCounter.Now - AggregationWindowMilliseconds / 2;

            if ( EMAAlpha < EMAAlphaMax )
            {
                UpdateAlpha( EMAAlpha + 0.05f );
            }
        }

        private float AggregationWindowBitrate()
        {
            var delta = AggregationWindowStart.DeltaToNowMilliseconds / 1000f; // float seconds
            var windowbitrate = 8f * ( AggregationWindowDataBytes / delta );
            return windowbitrate;
        }

        public override string ToString()
        {
            return string.Format( "Bitrate {0:###,###,##0.0}kBps ({1:#0.0}), a/w {2:###,###,##0.0} / {3:###,###,##0.0}.",
                Bitrate / 8192f, BitrateMax / 8192f, AggregatedBitrate / 8192f, AggregationWindowBitrate() / 8192f );
        }
    }
}

