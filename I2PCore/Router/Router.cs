using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel;
using I2PCore.Utils;
using I2PCore.Transport;
using I2PCore.Data;

namespace I2PCore.Router
{
    public class Router
    {
        static bool Started = false;
        public static void Start()
        {
            if ( Started ) return;

            var rci = RouterContext.Inst;
            NetDb.Start();

            DebugUtils.Log( "I: " + RouterContext.Inst.MyRouterInfo.ToString() );
            DebugUtils.Log( "Published: " + RouterContext.Inst.Published.ToString() );

            DebugUtils.Log( "Connecting..." );
            TransportProvider.Start();
            TunnelProvider.Start();

            Started = true;
        }

        public static ClientDestination CreateDestination( I2PDestinationInfo dest, bool publish )
        {
            return TunnelProvider.Inst.ClientsMgr.CreateDestination( dest, publish );
        }
    }
}
