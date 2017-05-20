using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class BandwidthStatistics
    {
        public Bandwidth ReceiveBandwidth = new Bandwidth();
        public Bandwidth SendBandwidth = new Bandwidth();

        DateTime StartTime = DateTime.Now;

        public BandwidthStatistics()
        {
        }

        public void DataReceived( int size )
        {
            ReceiveBandwidth.Measure( size );
        }

        public void DataSent( int size )
        {
            SendBandwidth.Measure( size );
        }

        public override string ToString()
        {
            return string.Format( "BwStat recv: {0} send: {1}", ReceiveBandwidth, SendBandwidth );
        }
    }
}
