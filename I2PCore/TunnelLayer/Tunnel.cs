using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.SessionLayer;

namespace I2PCore.TunnelLayer
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
                return ExpectedTunnelBuildTimePerHop * 8;
            }
        }

        // Time per hop for "ok" routers
        // Avg     1223 ms
        // StdDev  1972 ms
        public static TickSpan ExpectedTunnelBuildTimePerHop
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
                var result = !Terminated 
                    && Established
                    && CreationTime.DeltaToNow < ( Lifetime - TunnelRecreationMargin );
                return result;
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
                return EstablishedTime.DeltaToNow > Lifetime * 1.1 || 
                    CreationTime.DeltaToNow > Lifetime * 1.2; 
            } 
        }

        public TunnelConfig.TunnelPool Pool { get { return Config.Pool; } }
        public TunnelConfig.TunnelDirection TunnelDirection { get { return Config.Direction; } }

        // Statistics
        public readonly TunnelQuality Metrics = new TunnelQuality();

        // Info
        public TunnelConfig Config { get; private set; }
        public abstract IEnumerable<I2PRouterIdentity> TunnelMembers { get; }

        public readonly TickCounter CreationTime = TickCounter.Now;
        public TickCounter EstablishedTime = TickCounter.Now;

        public I2PTunnelId ReceiveTunnelId;

        public readonly int TunnelSeqNr;
        public readonly string TunnelDebugTrace;

        public static readonly BandwidthStatistics BandwidthTotal = new BandwidthStatistics();
        public readonly BandwidthStatistics Bandwidth = new BandwidthStatistics( BandwidthTotal );

        internal int AggregateErrors = 0;
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

        public virtual void MessageReceived( I2NPMessage msg, int recvdatasize )
        {
#if LOG_ALL_TUNNEL_TRANSFER
            Logging.LogDebug( $"{this}: MessageReceived {msg}" );
#endif
            Bandwidth.DataReceived( recvdatasize );

            //Logging.LogDebug( $"{this}: MessageReceived {msg.MessageType} TDM len {recvsize}" );
            ReceiveQueue.Enqueue( msg );
        }

        private static int TunnelIdCounter = 1;

        protected int TunnelMemberHops;

        protected ConcurrentQueue<I2NPMessage> ReceiveQueue = new ConcurrentQueue<I2NPMessage>();

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
