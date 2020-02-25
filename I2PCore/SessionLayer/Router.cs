using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer;
using I2PCore.Utils;
using I2PCore.TransportLayer;
using I2PCore.Data;
using System.Threading;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;

namespace I2PCore.SessionLayer
{
    public class Router
    {
        public static bool Started { get; protected set; }

        static ClientTunnelProvider ClientMgr;
        static ExplorationTunnelProvider ExplorationMgr;
        static TransitTunnelProvider TransitTunnelMgr;
        private static Thread Worker;

        public static event Action<DeliveryStatusMessage> DeliveryStatusReceived;

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
            TunnelProvider.I2NPMessageReceived += TunnelProvider_I2NPMessageReceived;
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
            var newclient = new ClientDestination( ClientMgr, dest, publish );
            ClientMgr.AttachClient( newclient );
            return newclient;
        }

        public static ClientOrigin CreateOrigin( I2PDestinationInfo mydest, I2PDestination remotedest )
        {
            var newclient = new ClientOrigin( ClientMgr, mydest, remotedest );
            ClientMgr.AttachClient( newclient );
            return newclient;
        }

        static void TunnelProvider_I2NPMessageReceived( II2NPHeader msg )
        {
            switch ( msg.MessageType )
            {
                case I2NPMessage.MessageTypes.DatabaseStore:
                    var ds = (DatabaseStoreMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"Router: DatabaseStore : {ds.Key.Id32Short}" );
#endif
                    HandleDatabaseStore( ds );
                    break;

                case I2NPMessage.MessageTypes.DatabaseSearchReply:
                    var dsr = (DatabaseSearchReplyMessage)msg.Message;
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.Log( $"Router: DatabaseSearchReply: {dsr}" );
#endif
                    NetDb.Inst.AddDatabaseSearchReply( dsr );
                    break;

                case I2NPMessage.MessageTypes.DeliveryStatus:
#if LOG_ALL_TUNNEL_TRANSFER || LOG_ALL_LEASE_MGMT
                    Logging.LogDebug( $"Router: DeliveryStatus: {msg.Message}" );
#endif

                    var dsmsg = (DeliveryStatusMessage)msg.Message;
                    DeliveryStatusReceived?.Invoke( dsmsg );
                    break;

                case I2NPMessage.MessageTypes.Garlic:
#if LOG_ALL_TUNNEL_TRANSFER
                    Logging.LogDebug( $"Router: Garlic: {msg.Message}" );
#endif
                    HandleGarlic( (GarlicMessage)msg.Message );
                    break;

                default:
                    Logging.LogDebug( $"TunnelProvider_I2NPMessageReceived: Unhandled message ({msg.Message})" );
                    break;
            }
        }

        internal static void HandleDatabaseStore( DatabaseStoreMessage ds )
        {
            if ( ds.RouterInfo == null && ds.LeaseSet == null ) throw new ArgumentException( "DatabaseStore without Router or Lease info!" );

            if ( ds.RouterInfo != null )
            {
#if LOG_ALL_TUNNEL_TRANSFER
                //Logging.Log( "HandleDatabaseStore: DatabaseStore RouterInfo" + ds.ToString() );
#endif
                NetDb.Inst.AddRouterInfo( ds.RouterInfo );
            }
            else
            {
#if LOG_ALL_TUNNEL_TRANSFER
                //Logging.Log( "HandleDatabaseStore: DatabaseStore LeaseSet" + ds.ToString() );
#endif
                NetDb.Inst.AddLeaseSet( ds.LeaseSet );
            }

            if ( ds.ReplyToken != 0 )
            {
                if ( ds.ReplyTunnelId != 0 )
                {
                    var outtunnel = TunnelProvider.Inst.GetEstablishedOutboundTunnel( true );
                    if ( outtunnel != null )
                    {
                        outtunnel.Send( new TunnelMessageRouter(
                            ( new TunnelGatewayMessage(
                                new DeliveryStatusMessage( ds.ReplyToken ),
                                ds.ReplyTunnelId ) ),
                            ds.ReplyGateway ) );
                    }
                }
                else
                {
                    TransportProvider.Send( ds.ReplyGateway,
                        new DeliveryStatusMessage( ds.ReplyToken ) );
                }
            }
        }

        private static void HandleGarlic( GarlicMessage garlicmsg )
        {
            try
            {
                // No sessions, just accept EG
                var (aesblock,sessionkey) = Garlic.EGDecryptGarlic(
                        garlicmsg.Garlic.EGData,
                        RouterContext.Inst.PrivateKey );

                if ( aesblock == null ) return;

                var garlic = new Garlic( (BufRefLen)aesblock.Payload );

#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"Router: HandleGarlic: {garlic}" );
#endif

                foreach ( var clove in garlic.Cloves )
                {
                    try
                    {
                        // Only accept Local to not turn into a bot net.
                        switch ( clove.Delivery.Delivery )
                        {
                            case GarlicCloveDelivery.DeliveryMethod.Local:
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"Router: HandleGarlic: Delivered Local: {clove.Message}" );
#endif
                                TunnelProvider.Inst.DistributeIncomingMessage( null, clove.Message.CreateHeader16 );
                                break;

                            default:
                                Logging.LogDebug( $"Router HandleGarlic: Dropped clove ({clove})" );
                                break;

                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( "Router: HandleGarlic switch", ex );
                    }
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( "Router: HandleGarlic", ex );
            }
        }
    }
}
