using System;
using System.Collections.Concurrent;
using System.Linq;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Collections.Generic;
using static System.Configuration.ConfigurationManager;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        static readonly TimeSpan MinLeaseLifetime = TimeSpan.FromMinutes( 3 );

        static TickSpan MinTimeBetweenFloodfillLSUpdates = TickSpan.Seconds( 20 );

        public static TickSpan InitiatorInactivityLimit = TickSpan.Minutes( 5 );

        public int LowWatermarkForNewTags { get; set; } = 7;
        public int NewTagsWhenGenerating { get; set; } = 15;

        public delegate void DestinationDataReceived( ClientDestination dest, BufLen data );

        /// <summary>
        /// Data was received by this Destination.
        /// </summary>
        public event DestinationDataReceived DataReceived;

        /// <summary>
        /// Used for debugging. Debug messages from the session layer gets tagged
        /// with this name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="T:I2PCore.SessionLayer.ClientDestination"/>
        /// automatic idle update remotes with the latest published leases.
        /// </summary>
        /// <value><c>true</c> if automatic idle update remotes; otherwise, <c>false</c>.</value>
        public bool AutomaticIdleUpdateRemotes { get; set; } = false;

        /// <summary>
        /// New inbound tunnels was established. To be able to publish them
        /// the user of the router must sign the leases and assign to SignedLeases
        /// as the private temporary key is not available to the router.
        /// </summary>
        public event Action<ClientDestination, IEnumerable<ILease>> SignLeasesRequest;

        /// <summary>
        /// Client states.
        /// </summary>
        public enum ClientStates { NoTunnels, NoLeases, Established, BuildingTunnels }

        /// <summary>
        /// Occurs when client state changed.
        /// </summary>
        public event Action<ClientDestination, ClientStates> ClientStateChanged;

        /// <summary>
        /// ls is null if lookup failed.
        /// </summary>
        public delegate void DestinationLookupResult( I2PIdentHash id, ILeaseSet ls, object tag );

        /// <summary>
        /// Gets or sets the inbound tunnel hop count.
        /// </summary>
        /// <value>The inbound tunnel hop count.</value>
        public int InboundTunnelHopCount { get; set; } = 3;

        /// <summary>
        /// Gets or sets the outbound tunnel hop count.
        /// </summary>
        /// <value>The outbound tunnel hop count.</value>
        public int OutboundTunnelHopCount { get; set; } = 3;

        /// <summary>
        /// Gets or sets the target active (not expired) inbound tunnel count.
        /// </summary>
        /// <value>Gets or sets the target active (not expired) inbound tunnel count.</value>
        public int TargetInboundTunnelCount { get; set; } = 2;

        /// <summary>
        /// Gets or sets the target active (not expired) outbound tunnel count.
        /// </summary>
        /// <value>Gets or sets the target active (not expired) outbound tunnel count.</value>
        public int TargetOutboundTunnelCount { get; set; } = 2;

        /// <summary>
        /// Currently established Leases for this Destination.
        /// </summary>
        /// <value>The leases.</value>
        public IEnumerable<ILease> EstablishedLeases
        {
            get
            {
                return EstablishedLeasesField;
            }
        }
        protected List<ILease> EstablishedLeasesField = new List<ILease>();

        /// <summary>
        /// Temporary public keys matching the <see cref="T:I2PCore.SessionLayer.ClientDestination.PrivateKeys"/>.
        /// These keys are included when new LeaseSets are generated if automatic LeaseSet signing is used.
        /// </summary>
        public List<I2PPublicKey> PublicKeys
        {
            get => MySessions.PublicKeys;
            set => MySessions.PublicKeys = value;
        }

        /// <summary>
        /// Temporary private keys matching the <see cref="T:I2PCore.SessionLayer.ClientDestination.PublicKeys"/>
        /// supplied with our LeaseSet. These keys are used to decrypt incomming commmunication.
        /// </summary>
        public List<I2PPrivateKey> PrivateKeys 
        { 
            get => MySessions.PrivateKeys;
            set => MySessions.PrivateKeys = value;
        }
        public void GenerateTemporaryKeys()
        {
            MySessions.GenerateTemporaryKeys();
        }

        /// <summary>
        /// Currently signed Leases for this Destination.
        /// If you are manually signing, <see cref="T:I2PCore.SessionLayer.ClientDestination.PrivateKeys"/>
        /// must be assigned as well at startup, or when the temporary keys change.
        /// </summary>
        /// <value>The lease set.</value>
        public ILeaseSet SignedLeases
        {
            get => SignedLeasesField;
            set
            {
                TestLeaseSet( value );

                if ( IsSameLeaseSet( SignedLeasesField, value ) )
                    return;

                SignedLeasesField = value;
                MySessions.MySignedLeasesUpdated();

                if ( PublishDestination && EstablishedLeases.Count() >= TargetInboundTunnelCount )
                {
                    NetDb.Inst.FloodfillUpdate.TrigUpdateLeaseSet( SignedLeases );
                }
            }
        }
        public ILeaseSet SignedLeasesField = null;

        /// <summary>
        /// I2P Destination address for this instance.
        /// </summary>
        /// <value>The destination.</value>
        public I2PDestination Destination { get; private set; }

        public readonly bool PublishDestination;

        readonly protected ConcurrentDictionary<OutboundTunnel, byte> OutboundPending =
                new ConcurrentDictionary<OutboundTunnel, byte>();
        readonly protected ConcurrentDictionary<InboundTunnel, byte> InboundPending =
                new ConcurrentDictionary<InboundTunnel, byte>();
        readonly protected ConcurrentDictionary<OutboundTunnel, byte> OutboundEstablishedPool =
                new ConcurrentDictionary<OutboundTunnel, byte>();
        readonly protected ConcurrentDictionary<InboundTunnel, byte> InboundEstablishedPool =
                new ConcurrentDictionary<InboundTunnel, byte>();

        internal readonly SessionManager MySessions;

        /// <summary>
        /// If null, the leases have to be explicitly signed by the user.
        /// </summary>
        /// <value>The this destination info.</value>
        protected I2PDestinationInfo ThisDestinationInfo { get; private set; }

        // Sign leases yourself
        internal ClientDestination(
                I2PDestination dest,
                bool publishdest )
        {
            Destination = dest;
            PublishDestination = publishdest;

            MySessions = new SessionManager( this );

            EstablishedLeasesField = new List<ILease>();

            ReadAppConfig();

            NetDb.Inst.LeaseSetUpdates += Ext_LeaseSetUpdates;
            NetDb.Inst.IdentHashLookup.LeaseSetReceived += Ext_LeaseSetUpdates;
        }

        // Let the router sign leases
        internal ClientDestination(
                I2PDestinationInfo destinfo,
                bool publishdest ): this( destinfo.Destination, publishdest )
        {
            ThisDestinationInfo = destinfo;

            MySessions.GenerateTemporaryKeys();
        }

        public bool Terminated { get; protected set; } = false;

        /// <summary>
        /// Shutdown this instance.
        /// </summary>
        public void Shutdown()
        {
            NetDb.Inst.IdentHashLookup.LeaseSetReceived -= Ext_LeaseSetUpdates;
            NetDb.Inst.LeaseSetUpdates -= Ext_LeaseSetUpdates;

            Router.ShutdownClient( this );
            Terminated = true;
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

        private ClientStates ClientStateField = ClientStates.NoTunnels;

        /// <summary>
        /// Gets the state of the client.
        /// </summary>
        /// <value>The state of the client.</value>
        public ClientStates ClientState
        {
            get
            {
                return ClientStateField;
            }
            internal set
            {
                if ( ClientStateField == value ) return;

                ClientStateField = value;
                ClientStateChanged?.Invoke( this, ClientStateField );
            }
        }

        PeriodicAction QueueStatusLog = new PeriodicAction( TickSpan.Seconds( 15 ) );

        PeriodicAction KeepClientStateUpdated = new PeriodicAction( TickSpan.Seconds( 10 ) );
        protected void UpdateClientState()
        {
            if ( InboundEstablishedPool.IsEmpty || OutboundEstablishedPool.IsEmpty )
            {
                ClientState = ClientStates.NoTunnels;
                return;
            }

            if ( !( (IClient)this ).ClientTunnelsStatusOk )
            {
                ClientState = ClientStates.BuildingTunnels;
                return;
            }

            ClientState = ClientStates.Established;
        }

        /// <summary>
        /// Send data to the destination.
        /// </summary>
        /// <returns>The send.</returns>
        /// <param name="dest">Destination.</param>
        /// <param name="buf">Buffer.</param>
        public ClientStates Send( I2PDestination dest, byte[] buf )
        {
            return Send( dest, new BufLen( buf ) );
        }

        /// <summary>
        /// Send data to the destination.
        /// </summary>
        /// <returns>The send.</returns>
        /// <param name="dest">Destination.</param>
        /// <param name="buf">Buffer.</param>
        public ClientStates Send( I2PDestination dest, BufLen buf )
        {
            if ( Terminated ) throw new InvalidOperationException( $"Destination {this} is terminated." );

            var clove = new GarlicClove(
                 new GarlicCloveDeliveryDestination(
                     new DataMessage( buf ),
                     dest.IdentHash ) );

            var result = Send( dest, clove );

            // Do not call this Send internally as it will keep the session going forever
            MySessions.DataSentToRemote( dest.IdentHash );

            return result;
        }

        /// <summary>
        /// Lookups the destination. cb will be called on success or timeout
        /// with the supplied tag.
        /// </summary>
        /// <param name="dest">Destination.</param>
        /// <param name="cb">Cb.</param>
        /// <param name="tag">Tag.</param>
        /// <return>True if a new search was started.</return>
        public bool LookupDestination(
                I2PIdentHash dest,
                DestinationLookupResult cb,
                object tag = null )
        {
            if ( cb is null ) return false;

            var lls = MySessions.GetLeaseSet( dest );
            if ( lls != null && NetDb.AreLeasesGood( lls ) )
            {
                cb?.Invoke( dest, lls, tag );
                return true;
            }

            return Router.StartDestLookup( dest, cb, tag );
        }

        protected void HandleDestinationLookupResult(
                I2PIdentHash hash,
                ILeaseSet ls,
                object tag )
        {
            if ( ls is null || ls.Expire < DateTime.UtcNow )
            {
                Logging.LogDebug(
                    $"{this}: Lease set lookup failed. Giving up." );

                return;
            }

            Logging.LogDebug(
                    $"{this}: Lease set for {hash.Id32Short} found ({ls.Expire})." );

            MySessions.LeaseSetReceived( ls );
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Name} {Destination.IdentHash.Id32Short}";
        }
    }
}
