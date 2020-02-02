using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class PeriodicAction
    {
        public delegate void PerformAction();

        int mFrequencyMilliSeconds;
        public int FrequencySeconds { get { return mFrequencyMilliSeconds / 1000; } set { mFrequencyMilliSeconds = value * 1000; } }

        public TickCounter LastAction { get; protected set; }
        public TickSpan TimeToAction
        {
            get => LastAction.DeltaToNow;
            set
            {
                LastAction = TickCounter.Now - TickSpan.Milliseconds( mFrequencyMilliSeconds ) + value;
            }
        }
        bool Autotrigger;

        protected PeriodicAction( int freqmsec, bool hastimedout )
        {
            mFrequencyMilliSeconds = freqmsec;
            Autotrigger = hastimedout;
            LastAction = new TickCounter();
        }

        public PeriodicAction( TickSpan freq ): this( freq.ToMilliseconds, false ) { }
        public PeriodicAction( TickSpan freq, bool hastimedout ) : this( freq.ToMilliseconds, hastimedout ) { }

        public void Reset()
        {
            LastAction = new TickCounter();
        }

        public void Start()
        {
            if ( LastAction == null ) 
                LastAction = new TickCounter();
            else
                LastAction.SetNow();
        }

        public void Stop()
        {
            LastAction = null;
        }

        public void Do( PerformAction action )
        {
            if ( LastAction == null ) return;

            if ( Autotrigger || LastAction.DeltaToNowMilliseconds > mFrequencyMilliSeconds )
            {
                LastAction.SetNow();
                action();
                Autotrigger = false;
            }
        }
    }
}
