using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.Utils;
using I2PCore.TransportLayer;
using static System.Configuration.ConfigurationManager;

namespace I2PCore.SessionLayer
{
    public sealed class ClientDestination: Client
    {
        public delegate void DestinationDataReceived( BufLen data );

        /// <summary>
        /// Data was received by this Destination from a remote Origin.
        /// </summary>
        public event DestinationDataReceived DataReceived;

        readonly bool PublishDestination;

        readonly ClientTunnelProvider ClientTunnelMgr;

        internal ClientDestination( 
            ClientTunnelProvider tp, 
            I2PDestinationInfo destinfo, 
            bool publishdest ): base( destinfo )
        {
            ClientTunnelMgr = tp;
            PublishDestination = publishdest;

            ReadAppConfig();
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( AppSettings["InboundTunnelsPerDestination"] ) )
            {
                TargetInboundTunnelCount = int.Parse( AppSettings["InboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["OutboundTunnelsPerDestination"] ) )
            {
                TargetOutboundTunnelCount = int.Parse( AppSettings["OutboundTunnelsPerDestination"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["InboundTunnelHops"] ) )
            {
                InboundTunnelHopCount = int.Parse( AppSettings["InboundTunnelHops"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["OutboundTunnelHops"] ) )
            {
                OutboundTunnelHopCount = int.Parse( AppSettings["OutboundTunnelHops"] );
            }
        }

        public override void Execute()
        {
            base.Execute();
        }

        internal override void DestinationMessageReceived( I2NPMessage message )
        {
#if LOG_ALL_LEASE_MGMT
            Logging.LogDebug( $"{this}: DestinationMessageReceived: {msg}" );
#endif
            if ( message.MessageType == I2NPMessage.MessageTypes.Data && DataReceived != null )
            {
                var dmsg = (DataMessage)message;
                DataReceived( dmsg.DataMessagePayload );
            }

            if ( message.MessageType == I2NPMessage.MessageTypes.DatabaseStore )
            {
                var dbsmsg = (DatabaseStoreMessage)message;

                if ( dbsmsg.LeaseSet.Leases.Any() )
                {
                    NetDb.Inst.AddLeaseSet( dbsmsg.LeaseSet );

                    // TODO: Fix
                    var lsupdate = new DatabaseStoreMessage( Leases );

                    var garlic = new Garlic(
                        new GarlicClove(
                            new GarlicCloveDeliveryDestination(
                                lsupdate,
                                dbsmsg.LeaseSet.Destination.IdentHash ) )
                    );

                    var egmsg = Garlic.EGEncryptGarlic(
                            garlic,
                            dbsmsg.LeaseSet.Destination.PublicKey,
                            new I2PSessionKey(),
                            null );

                    var outtunnel = SelectOutboundTunnel();
                    var intunnel = SelectLease( dbsmsg.LeaseSet );

                    Logging.LogDebug( $"Sending Garlic from tunnel {outtunnel} to {intunnel}" );

                    outtunnel?.Send(
                        new TunnelMessageTunnel(
                            new GarlicMessage( egmsg ),
                            intunnel.TunnelGw, intunnel.TunnelId ) );
                }
            }
        }

        protected override void LeaseSetUpdated( bool newlease )
        {
            if ( newlease && PublishDestination )
            {
                NetDb.Inst.FloodfillUpdate.TrigUpdateLeaseSet( Leases );
            }
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Destination.IdentHash.Id32Short}";
        }
    }
}
