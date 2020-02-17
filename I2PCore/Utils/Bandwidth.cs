using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace I2PCore.Utils
{
    public class Bandwidth
    {
        static readonly TickSpan AggregationWindow = TickSpan.Seconds( 20 );
        TickCounter AggregationWindowStart = new TickCounter();
        private long AggregationWindowDataBytes;
        float AggregatedBitrate = 0f;

        readonly static float EMAAlphaMax = 0.6f;
        float EMAAlpha;
        float EMAAlphaRes;

        private long DataBytesField;
        public long DataBytes { get => DataBytesField; }

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

        private void UpdateAlpha( float v )
        {
            EMAAlpha = v;
            EMAAlphaRes = 1f - v;
        }

        private readonly SemaphoreSlim UpdateGate = new SemaphoreSlim( 1, 1 );

        public void Measure( long size )
        {
            Interlocked.Add( ref DataBytesField, size );
            Interlocked.Add( ref AggregationWindowDataBytes, size );

            if ( UpdateGate.Wait( 0 ) )
            {
                try
                {
                    if ( AggregationWindowStart.DeltaToNow > AggregationWindow )
                    {
                        UpdateAggregatedBitrate();
                    }
                }
                finally
                {
                    UpdateGate.Release();
                }
            }
        }

        private void UpdateAggregatedBitrate()
        {
            var windowbitrate = AggregationWindowBitrate();
            AggregatedBitrate = ( AggregatedBitrate * EMAAlpha ) + windowbitrate * EMAAlphaRes;

            AggregationWindowDataBytes /= 2;
            AggregationWindowStart = TickCounter.Now - AggregationWindow / 2;

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
            return $"{Bitrate:###,###,##0.0}Bps ({BitrateMax:#0.0}), a/w " +
                $"{AggregatedBitrate:###,###,##0.0} / {AggregationWindowBitrate():###,###,##0.0}.";
        }
    }
}

