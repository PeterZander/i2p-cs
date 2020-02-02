using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class PeriodicLogger
    {
        PeriodicAction LogAction;
        readonly Logging.LogLevels LogLevel;

        public PeriodicLogger( int freqsec )
        {
            LogAction = new PeriodicAction( TickSpan.Seconds( freqsec ) );
            LogLevel = Logging.LogLevels.DebugData;
        }

        public PeriodicLogger( Logging.LogLevels level, int freqsec )
        {
            LogLevel = level;
            LogAction = new PeriodicAction( TickSpan.Seconds( freqsec ) );
        }

        public void Log( Func<string> maker )
        {
            LogAction.Do( () => Logging.Log( LogLevel, maker() ) );
        }
    }
}
