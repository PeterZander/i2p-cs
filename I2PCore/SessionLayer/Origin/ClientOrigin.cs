using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Linq;
using static System.Configuration.ConfigurationManager;

namespace I2PCore.SessionLayer
{
    public class ClientOrigin: Client
    {
        public int LowWatermarkForNewTags { 
            get => SessionKeys.LowWatermarkForNewTags;
            set => SessionKeys.LowWatermarkForNewTags = value; 
        }

        public int NewTagsWhenGenerating
        {
            get => SessionKeys.NewTagsWhenGenerating;
            set => SessionKeys.NewTagsWhenGenerating = value;
        }

        public ClientTunnelProvider ClientTunnelMgr { get; }

        readonly I2PDestination RemoteDestination;

        internal ClientOrigin( 
                ClientTunnelProvider tp,
                I2PDestinationInfo mydestinfo,
                I2PDestination remotedest ): base( mydestinfo )
        {
            ClientTunnelMgr = tp;
            RemoteDestination = remotedest;
            SessionKeys = new SessionKeyOrigin( Destination, remotedest );

            ReadAppConfig();

            LookupDestination( RemoteDestination.IdentHash, HandleDestinationLookupResult );
        }

        public bool Send( byte[] buf )
        {
            return Send( new BufLen( buf ) );
        }

        readonly SessionKeyOrigin SessionKeys;

        public bool Send( BufLen buf )
        {
            if ( InboundEstablishedPool.IsEmpty )
            {
                Logging.LogDebug( $"{this}: No inbound tunnels available." );
                return false;
            }

            var rl = MyRemoteDestinations.GetLeases( RemoteDestination );

            if ( rl is null )
            {
                Logging.LogDebug( $"{this}: No leases available." );
                LookupDestination( RemoteDestination.IdentHash, HandleDestinationLookupResult );
                return false;
            }

            var outtunnel = SelectOutboundTunnel();

            if ( outtunnel is null )
            {
                Logging.LogDebug( $"{this}: No outbound tunnels available." );
                return false;
            }

            var remotelease = SelectLease( rl );

            if ( remotelease is null )
            {
                Logging.LogDebug( $"{this}: No remote leases available." );
                return false;
            }

           var clove = new GarlicClove(
                new GarlicCloveDeliveryDestination(
                    new DataMessage( buf ),
                    RemoteDestination.IdentHash ) );

            return SessionKeys.Send( 
                    outtunnel, 
                    remotelease,
                    () =>
                    {
                        var gwtunnel = SelectInboundTunnel();
                        return (gwtunnel.Destination, gwtunnel.GatewayTunnelId);
                    },
                    clove );
        }

        public void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( AppSettings["InboundTunnelsPerOrigin"] ) )
            {
                TargetInboundTunnelCount = int.Parse( AppSettings["InboundTunnelsPerOrigin"] );
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings["OutboundTunnelsPerOrigin"] ) )
            {
                TargetOutboundTunnelCount = int.Parse( AppSettings["OutboundTunnelsPerOrigin"] );
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

        PeriodicAction PurgeExpiredLeases = new PeriodicAction( TickSpan.Seconds( 5 ) );

        public override void Execute()
        {
            base.Execute();

            PurgeExpiredLeases.Do( () =>
            {
                var rl = MyRemoteDestinations.GetLeases( RemoteDestination );

                if ( rl is null ) return;

                rl.RemoveExpired();
                if ( !rl.Leases.Any() )
                {
                    Logging.LogDebug( $"{this}: All remote leases expired. Looking up again." );
                    LookupDestination( RemoteDestination.IdentHash, HandleDestinationLookupResult );
                }

                UpdateClientState();
            } );

            LookupRemoteDestinationLeases?.Do( () =>
            {
                Logging.LogDebug(
                    $"{this}: Destination leases unknown. Looking up again." );

                LookupDestination(
                    RemoteDestination.IdentHash,
                    HandleDestinationLookupResult );
            } );
        }

        PeriodicAction LookupRemoteDestinationLeases = null;

        void HandleDestinationLookupResult( I2PIdentHash hash, I2PLeaseSet ls )
        {
            if ( ls is null )
            {
                Logging.LogDebug(
                    $"{this}: Lease set lookup failed. Starting timer." );

                LookupRemoteDestinationLeases = new PeriodicAction( TickSpan.Minutes( 1 ) );
                return;
            }

            Logging.LogDebug(
                    $"{this}: Lease set for {RemoteDestination} found." );

            LookupRemoteDestinationLeases = null;
            MyRemoteDestinations.LeaseSetReceived( ls );
        }

        internal override void DestinationMessageReceived( I2NPMessage message )
        {
            /*
            if ( message.MessageType == I2NPMessage.MessageTypes.DeliveryStatus )
            {
                SessionKeys.
            }*/
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Destination.IdentHash.Id32Short} -> {RemoteDestination.IdentHash.Id32Short}";
        }

        protected override void LeaseSetUpdated( bool newlease )
        {
        }
    }
}
