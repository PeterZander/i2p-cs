using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP;
using I2PCore.Data;
using I2PCore.Transport;
using I2PCore.Tunnel.I2NP.Data;
using System.Threading;
using I2PCore.Utils;

namespace I2PCore.Tunnel
{
    public abstract class Tunnel
    {
        /// <summary>
        /// Inbound gateway for inbound tunnels. Outbound next hop for outbound tunnels.
        /// </summary>
        public abstract I2PIdentHash Destination { get; }

        // "each hop expires the tunnel after 10 minutes" https://geti2p.net/spec/tunnel-creation
        public const int TunnelLifetimeSeconds = 10 * 60;
        public const int TunnelRecreationMarginSeconds = 60 * 2;
        public const int TunnelRecreationMarginSecondsPerHop = MeassuredTunnelBuildTimePerHopSeconds * 8;
        public const int MeassuredTunnelBuildTimePerHopSeconds = 20; // ema ~5-6s, >20s responses tend to be really slow.

        public virtual int LifetimeSeconds { get { return TunnelLifetimeSeconds; } }

        public virtual bool NeedsRecreation
        {
            get
            {
                var panic = Config.Pool == TunnelConfig.TunnelPool.Client && !TunnelProvider.Inst.ClientsMgr.ClientTunnelsStatusOk;

                return CreationTime.DeltaToNowSeconds > (
                    LifetimeSeconds -
                    ( panic ? TunnelRecreationMarginSeconds * 2 : TunnelRecreationMarginSeconds ) -
                    TunnelRecreationMarginSecondsPerHop * TunnelMemberHops );
            }
        }

        public virtual bool Active
        {
            get
            {
                return CreationTime.DeltaToNowSeconds < ( LifetimeSeconds - TunnelRecreationMarginSeconds );
            }
        }

        public virtual bool Expired 
        { 
            get 
            {
                return EstablishedTime.DeltaToNowSeconds > LifetimeSeconds || 
                    CreationTime.DeltaToNowSeconds > LifetimeSeconds * 1.1; 
            } 
        }

        public TickSpan MinLatencyMeasured { set; get; }

        internal TunnelConfig Config;
        public abstract int TunnelEstablishmentTimeoutSeconds { get; }

        protected int TunnelMemberHops;
        public abstract IEnumerable<I2PRouterIdentity> TunnelMembers { get; }

        public I2PTunnelId ReceiveTunnelId;

        public TickCounter CreationTime = TickCounter.Now;
        public TickCounter EstablishedTime = TickCounter.Now;

        public readonly int TunnelSeqNr;
        public readonly string TunnelDebugTrace;
        static int TunnelIdCounter = 1;

        internal BandwidthStatistics Bandwidth = new BandwidthStatistics();

        protected LinkedList<TunnelMessage> SendQueue = new LinkedList<TunnelMessage>();
        protected LinkedList<I2NPMessage> SendRawQueue = new LinkedList<I2NPMessage>();
        protected LinkedList<II2NPHeader> ReceiveQueue = new LinkedList<II2NPHeader>();

        protected Tunnel( TunnelConfig config )
        {
            Config = config;
            TunnelMemberHops = config == null ? 1 : config.Info.Hops.Count;
            TunnelSeqNr = Interlocked.Increment( ref TunnelIdCounter );
            TunnelDebugTrace = "<" + TunnelSeqNr.ToString() + ">";
            if ( TunnelSeqNr > int.MaxValue - 100 ) TunnelIdCounter = 1;
        }

        protected bool Terminated = false;
        public virtual void Shutdown()
        {
            Terminated = true;
        }

        public virtual void MessageReceived( II2NPHeader msg )
        {
            Bandwidth.DataReceived( msg.HeaderAndPayload.Length );

            lock ( ReceiveQueue )
            {
                ReceiveQueue.AddFirst( msg );
            }
        }

        public virtual void Send( TunnelMessage msg )
        {
            lock ( SendQueue )
            {
                SendQueue.AddFirst( msg );
            }
        }

        public virtual void SendRaw( I2NPMessage msg )
        {
            lock ( SendQueue )
            {
                SendRawQueue.AddFirst( msg );
            }
        }

        public TunnelConfig.TunnelPool Pool { get { return Config.Pool; } }
        public TunnelConfig.TunnelDirection TunnelDirection { get { return Config.Direction; } }

        /// <summary>
        /// True: Tunnel is working. False: Tunnel has failed.
        /// </summary>
        /// <returns></returns>
        public abstract bool Exectue();

        public override string ToString()
        {
            string pool;
            if ( Config != null ) pool = Config.Pool.ToString(); else pool = "<?>";
            return this.GetType().Name + " " + pool + " " + TunnelDebugTrace;
        }
    }
}
