
using System.Threading;

namespace I2PCore.Utils
{
    public class SuccessRatio
    {
        long SuccessCountField;
        long FailureCountField;

        public long SuccessCount { get => Interlocked.Read( ref SuccessCountField ); }
        public long FailureCount { get => Interlocked.Read( ref FailureCountField ); }

        public long Success( bool succ ) => succ ? Success() : Failure();
        public long Success() => Interlocked.Increment( ref SuccessCountField );

        public long Failure() => Interlocked.Increment( ref FailureCountField );

        public double Ratio { get => (double)SuccessCountField / FailureCountField; }
        public double Percent { get => ( 100.0 * SuccessCountField ) / ( SuccessCountField + FailureCountField ); }

        public override string ToString()
        {
            return $"Succ: {SuccessCountField}, Fail: {FailureCountField}, Ratio: {Ratio:F2}, {Percent:F2}%";
        }
    }
}