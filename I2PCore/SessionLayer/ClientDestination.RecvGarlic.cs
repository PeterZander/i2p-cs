using System;
using System.Linq;
using I2PCore.TransportLayer;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Threading;
using System.Collections.Generic;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        void InboundTunnel_GarlicMessageReceived( GarlicMessage msg )
        {
            try
            {
                /*
                var info = FreenetBase64.Encode( new BufLen( ThisDestinationInfo.ToByteArray() ) );
                var g = FreenetBase64.Encode( new BufLen( msg.Garlic.ToByteArray() ) );
                var st = $"DestinationInfo:\r\n{string.Join( "\r\n", info.Chunks( 40 ).Select( s => $"\"{s}\" +" ) )}" +
                    $"\r\nGarlic (EG):\r\n{string.Join( "\r\n", g.Chunks( 40 ).Select( s => $"\"{s}\" +" ) )}";
                File.WriteAllText( $"Garlic_{DateTime.Now.ToLongTimeString()}.b64", st );
                */

                var decr = MySessions.DecryptMessage( msg );
                if ( decr == null )
                {
                    Logging.LogWarning( $"{this}: GarlicMessageReceived: Failed to decrypt garlic." );
                    return;
                }

#if LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this}: GarlicMessageReceived: {decr}: {string.Join( ',', decr.Cloves.Select( c => c.Message ) ) }" );
#endif
                List<DataMessage> DestinationMessages = null;

                foreach ( var clove in decr.Cloves )
                {
                    try
                    {
                        switch ( clove.Delivery.Delivery )
                        {
                            case GarlicCloveDelivery.DeliveryMethod.Local:
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: Delivered Local: {clove.Message}" );
#endif
                                TunnelProvider.Inst.DistributeIncomingMessage( null, clove.Message.CreateHeader16 );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Router:
                                var dest = ( (GarlicCloveDeliveryRouter)clove.Delivery ).Destination;
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: Delivered Router: {dest.Id32Short} {clove.Message}" );
#endif
                                ThreadPool.QueueUserWorkItem( a => TransportProvider.Send( dest, clove.Message ) );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Tunnel:
                                var tone = (GarlicCloveDeliveryTunnel)clove.Delivery;
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: " +
                                    $"Delivered Tunnel: {tone.Destination.Id32Short} " +
                                    $"TunnelId: {tone.Tunnel} {clove.Message}" );
#endif
                                ThreadPool.QueueUserWorkItem( a => TransportProvider.Send(
                                        tone.Destination,
                                        new TunnelGatewayMessage(
                                            clove.Message,
                                            tone.Tunnel ) ) );
                                break;

                            case GarlicCloveDelivery.DeliveryMethod.Destination:
#if LOG_ALL_LEASE_MGMT
                                Logging.LogDebug(
                                    $"{this}: GarlicMessageReceived: " +
                                    $"Delivered Destination: {clove.Message}" );
#endif
                                switch ( clove?.Message )
                                {
                                    case DatabaseStoreMessage dbsmsg when dbsmsg?.LeaseSet != null:
                                        MySessions.RemoteIsActive( dbsmsg.LeaseSet?.Destination?.IdentHash );

                                        if ( dbsmsg.LeaseSet.Expire > DateTime.UtcNow )
                                        {
                                            Logging.LogDebug( $"{this}: New lease set received in stream for {dbsmsg.LeaseSet.Destination} {dbsmsg.LeaseSet}." );
                                            MySessions.LeaseSetReceived( dbsmsg.LeaseSet );
                                            ThreadPool.QueueUserWorkItem( a => UpdateClientState() );
                                        }
                                        break;

                                    case DataMessage dmsg when DataReceived != null:
                                        if ( DestinationMessages is null )
                                                DestinationMessages = new List<DataMessage>();
                                        DestinationMessages.Add( dmsg );
                                        break;

                                    default:
                                        Logging.LogDebug( $"{this}: Garlic discarded {clove.Message}" );
                                        break;
                                }
                                break;
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( "ClientDestination GarlicDecrypt Clove", ex );
                    }
                }

                if ( DestinationMessages != null )
                {
                    ThreadPool.QueueUserWorkItem( a => 
                    {
                        foreach( var dmsg in DestinationMessages )
                        {
#if LOG_ALL_LEASE_MGMT
                            Logging.LogDebug( $"{this}: DestinationMessageReceived: {dmsg}" );
#endif
                            DataReceived?.Invoke( this, dmsg.DataMessagePayload );
                        }
                    } );
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( "ClientDestination GarlicDecrypt", ex );
            }
        }
    }
}