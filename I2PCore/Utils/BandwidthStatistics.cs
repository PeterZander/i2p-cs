using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static I2PCore.Utils.BufUtils;

namespace I2PCore.Utils
{
    public class BandwidthStatistics
    {
        public Bandwidth ReceiveBandwidth = new Bandwidth();
        public Bandwidth SendBandwidth = new Bandwidth();
        readonly DateTime StartTime = DateTime.Now;

        public event Action<int> OnDataReceived;
        public event Action<int> OnDataSent;

        public BandwidthStatistics()
        {
        }

        public BandwidthStatistics( BandwidthStatistics aggregate )
        {
            OnDataReceived += aggregate.DataReceived;
            OnDataSent += aggregate.DataSent;
        }

        public void DataReceived( int size )
        {
            ReceiveBandwidth.Measure( size );
            OnDataReceived?.Invoke( size );
        }

        public void DataSent( int size )
        {
            SendBandwidth.Measure( size );
            OnDataSent?.Invoke( size );
        }

        public override string ToString()
        {
            return $"send / recv: {SendBandwidth.Bitrate/1024f,8:0.00} /" +
                $"{ReceiveBandwidth.Bitrate/1024f,8:0.00} kbps       " +
                $"{BytesToReadable( SendBandwidth.DataBytes ),12} /" +
                $"{BytesToReadable( ReceiveBandwidth.DataBytes ),12} ";
        }
    }
}
