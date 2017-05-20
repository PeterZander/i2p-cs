using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class PeriodicLogger
    {
        public delegate string MakeMessage();

        PeriodicAction LogAction;
        DebugUtils.LogLevels LogLevel;

        public PeriodicLogger( int freqsec )
        {
            LogAction = new PeriodicAction( TickSpan.Seconds( freqsec ) );
            LogLevel = DebugUtils.LogLevels.DebugData;
        }

        public PeriodicLogger( DebugUtils.LogLevels level, int freqsec )
        {
            LogLevel = level;
            LogAction = new PeriodicAction( TickSpan.Seconds( freqsec ) );
        }

        public void Log( MakeMessage maker )
        {
            LogAction.Do( () => DebugUtils.Log( LogLevel, maker() ) );
        }
    }
}
