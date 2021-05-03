using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace I2PCore.Utils
{
    public class RunBatchWait
    {
        int InitialCount;
        int Counter;
        ManualResetEvent Finished;

        public RunBatchWait( int count )
        {
            InitialCount = count;
            Interlocked.Exchange( ref Counter, InitialCount );
            Finished = new ManualResetEvent( count == 0 );
        }

        public void Reset()
        {
            Interlocked.Exchange( ref Counter, InitialCount );
            Finished.Reset();
        }

        public void Set()
        {
            if ( Interlocked.Decrement( ref Counter ) == 0 )
            {
                Finished.Set();
            }
        }

        public bool WaitOne( int waitms )
        {
            return Finished.WaitOne( waitms );
        }
    }
}
