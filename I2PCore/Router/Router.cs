using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel;
using I2PCore.Utils;
using I2PCore.Transport;
using I2PCore.Data;
using System.Threading;

namespace I2PCore.Router
{
    public class Router
    {
        public static bool Started { get; protected set; }

        static ClientTunnelProvider ClientMgr;
        static ExplorationTunnelProvider ExplorationMgr;
        static TransitTunnelProvider TransitTunnelMgr;
        private static Thread Worker;

        public static void Start()
        {
            if ( Started ) return;

            try
            {
                var rci = RouterContext.Inst;
                NetDb.Start();

                Logging.Log( "I: " + RouterContext.Inst.MyRouterInfo.ToString() );
                Logging.Log( "Published: " + RouterContext.Inst.Published.ToString() );

                Logging.Log( "Connecting..." );
                TransportProvider.Start();
                TunnelProvider.Start();

                ClientMgr = new ClientTunnelProvider( TunnelProvider.Inst );
                ExplorationMgr = new ExplorationTunnelProvider( TunnelProvider.Inst );
                TransitTunnelMgr = new TransitTunnelProvider( TunnelProvider.Inst );

                Worker = new Thread( Run )
                {
                    Name = "Router",
                    IsBackground = true
                };
                Worker.Start();

                Started = true;
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
        }

        static bool Terminated = false;
        private static void Run()
        {
            try
            {
                Thread.Sleep( 2000 );

                while ( !Terminated )
                {
                    try
                    {
                        ClientMgr.Execute();
                        ExplorationMgr.Execute();
                        TransitTunnelMgr.Execute();

                        Thread.Sleep( 500 );
                    }
                    catch ( ThreadAbortException ex )
                    {
                        Logging.Log( ex );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
            }
            finally
            {
                Terminated = true;
            }
        }

        public static ClientDestination CreateDestination( I2PDestinationInfo dest, bool publish )
        {
            return ClientMgr.CreateDestination( dest, publish );
        }
    }
}
