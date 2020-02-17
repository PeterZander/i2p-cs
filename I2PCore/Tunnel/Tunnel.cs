using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Tunnel
{
    public abstract class Tunnel
    {
        /// <summary>
        /// Inbound gateway for inbound tunnels. Outbound next hop for outbound tunnels.
        /// </summary>
        public abstract I2PIdentHash Destination { get; }

        public abstract TickSpan TunnelEstablishmentTimeout { get; }

        // "each hop expires the tunnel after 10 minutes" https://geti2p.net/spec/tunnel-creation
        public static readonly TickSpan TunnelLifetime = TickSpan.Seconds( 10 * 60 );
        public static readonly TickSpan TunnelRecreationMargin = TickSpan.Seconds( 2 * 60 );
        public static TickSpan TunnelRecreationMarginPerHop
        {
            get
            {
                return MeassuredTunnelBuildTimePerHop * 8;
            }
        }

        // Time per hop for "ok" routers
        // Avg     1223 ms
        // StdDev  1972 ms
        public static TickSpan MeassuredTunnelBuildTimePerHop
        {
            get
            {
                return RouterContext.Inst.IsFirewalled 
                    ? TickSpan.Seconds( 5 )
                    : TickSpan.Seconds( 3 );
            }
        }

        public virtual TickSpan Lifetime { get { return TunnelLifetime; } }

        public virtual bool Active
        {
            get
            {
                return CreationTime.DeltaToNow < ( Lifetime - TunnelRecreationMargin );
            }
        }

        public virtual bool Established { get; set; }

        public virtual bool NeedsRecreation
        {
            get
            {
                var panic = Config.Pool == TunnelConfig.TunnelPool.Client
                    && !TunnelProvider.Inst.ClientTunnelsStatusOk;

                return CreationTime.DeltaToNow > (
                    Lifetime -
                    ( panic ? TunnelRecreationMargin * 2 : TunnelRecreationMargin ) -
                    TunnelRecreationMarginPerHop * TunnelMemberHops );
            }
        }

        public virtual bool Expired 
        { 
            get 
            {
                return EstablishedTime.DeltaToNow > Lifetime || 
                    CreationTime.DeltaToNow > Lifetime * 1.1; 
            } 
        }

        public TunnelConfig.TunnelPool Pool { get { return Config.Pool; } }
        public TunnelConfig.TunnelDirection TunnelDirection { get { return Config.Direction; } }

        // Statistics
        public TickSpan MinLatencyMeasured { set; get; }

        // Info
        public TunnelConfig Config { get; private set; }
        public abstract IEnumerable<I2PRouterIdentity> TunnelMembers { get; }

        public TickCounter CreationTime = TickCounter.Now;
        public TickCounter EstablishedTime = TickCounter.Now;

        public I2PTunnelId ReceiveTunnelId;

        public readonly int TunnelSeqNr;
        public readonly string TunnelDebugTrace;

        public static readonly BandwidthStatistics BandwidthTotal = new BandwidthStatistics();
        public readonly BandwidthStatistics Bandwidth = new BandwidthStatistics( BandwidthTotal );

        internal ITunnelOwner Owner { get; private set; }

        protected Tunnel( ITunnelOwner owner, TunnelConfig config )
        {
            Owner = owner;
            Config = config;
            TunnelMemberHops = config == null ? 1 : config.Info.Hops.Count;
            TunnelSeqNr = Interlocked.Increment( ref TunnelIdCounter );
            TunnelDebugTrace = $"<{TunnelSeqNr}>";
            if ( TunnelSeqNr > int.MaxValue - 100 ) TunnelIdCounter = 1;
        }

        public bool Terminated { get; private set; }

        public virtual void Shutdown()
        {
            Terminated = true;
        }

        public virtual void MessageReceived( II2NPHeader msg, int recvdatasize = -1 )
        {
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"{this}: MessageReceived {msg.Message}" );
#endif
            var recvsize = recvdatasize == -1
                ? msg.HeaderAndPayload.Length
                : recvdatasize;

            Bandwidth.DataReceived( recvsize );

            //Logging.LogDebug( $"{this}: MessageReceived {msg.MessageType} TDM len {recvsize}" );
            ReceiveQueue.Enqueue( msg );
        }

        public virtual void Send( TunnelMessage msg )
        {
            SendQueue.Enqueue( msg );
        }

        private static int TunnelIdCounter = 1;

        protected int TunnelMemberHops;

        protected ConcurrentQueue<TunnelMessage> SendQueue = new ConcurrentQueue<TunnelMessage>();
        protected ConcurrentQueue<II2NPHeader> ReceiveQueue = new ConcurrentQueue<II2NPHeader>();

        /// <summary>
        /// True: Tunnel is working. False: Tunnel has failed.
        /// </summary>
        /// <returns></returns>
        public abstract bool Exectue();

        public override string ToString()
        {
            string pool;
            if ( Config != null ) pool = Config.Pool.ToString(); else pool = "<?>";
            return $"{this.GetType().Name} {pool} {TunnelDebugTrace}";
        }
    }
}
