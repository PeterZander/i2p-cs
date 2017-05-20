using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore
{
    public class PublishedBandwidth
    {
        public class BandwidthRange
        {
            public int MinKBps;
            public int MaxKBps;
        }

        public class BandwithLetter
        {
            public BandwidthRange Range;
            public char Letter;
        }

        /*
            K: Under 12 KBps shared bandwidth
            L: 12 - 48 KBps shared bandwidth (default)
            M: 48 - 64 KBps shared bandwidth
            N: 64 - 128 KBps shared bandwidth
            O: 128 - 256 KBps shared bandwidth
            P: 256 - 2000 KBps shared bandwidth (as of release 0.9.20)
            X: Over 2000 KBps shared bandwidth (as of release 0.9.20)         
         */
        public static BandwithLetter[] DefinedBandwidths = new BandwithLetter[]
        {
            new BandwithLetter() { Letter = 'K', Range = new BandwidthRange() { MinKBps = 0, MaxKBps = 11 } },
            new BandwithLetter() { Letter = 'L', Range = new BandwidthRange() { MinKBps = 12, MaxKBps = 47 } },
            new BandwithLetter() { Letter = 'M', Range = new BandwidthRange() { MinKBps = 48, MaxKBps = 63 } },
            new BandwithLetter() { Letter = 'N', Range = new BandwidthRange() { MinKBps = 64, MaxKBps = 127 } },
            new BandwithLetter() { Letter = 'O', Range = new BandwidthRange() { MinKBps = 128, MaxKBps = 255 } },
            new BandwithLetter() { Letter = 'P', Range = new BandwidthRange() { MinKBps = 256, MaxKBps = 1999 } },
            new BandwithLetter() { Letter = 'X', Range = new BandwidthRange() { MinKBps = 2000, MaxKBps = int.MaxValue } },
        };
    }
}
