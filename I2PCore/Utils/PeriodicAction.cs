using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class PeriodicAction
    {
        public delegate void PerformAction();

        TickSpan mFrequency;
        TickSpan mOriginalFrequency = null;

        public TickSpan Frequency 
        { 
            get 
            { 
                return mFrequency;
            } 
            set 
            { 
                mFrequency = value;
            } 
        }

        public TickCounter LastAction { get; protected set; }
        public TickSpan TimeToAction
        {
            get => ( LastAction + mFrequency ).DeltaToNow;
            set
            {
                if ( mOriginalFrequency is null )
                {
                    mOriginalFrequency = mFrequency;
                }

                LastAction = TickCounter.Now;
                mFrequency = value;
            }
        }

        bool Autotrigger;

        public PeriodicAction( TickSpan freq, bool hastimedout = false )
        {
            mFrequency = freq;
            Autotrigger = hastimedout;
            LastAction = TickCounter.Now;
        }

        public void Reset()
        {
            LastAction = TickCounter.Now;
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

            if ( Autotrigger || LastAction.DeltaToNow > mFrequency )
            {
                LastAction.SetNow();
                Autotrigger = false;
                if ( mOriginalFrequency != null )
                {
                    mFrequency = mOriginalFrequency;
                    mOriginalFrequency = null;
                }

                action();
            }
        }
    }
}
