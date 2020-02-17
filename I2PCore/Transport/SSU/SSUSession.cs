using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using I2PCore.Transport.NTCP;
using System.Threading;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Router;
using I2PCore.Transport.SSU.Data;
using System.Collections.Concurrent;

namespace I2PCore.Transport.SSU
{
    public class SSUSession: ITransport
    {
#if DEBUG
        public const int SendQueueLengthWarningLimit = 50;

#if SSU_TRACK_OLD_MAC_KEYS
        internal enum OldKeyTypes { Undefined, Intro, Shared, MAC }
        internal class OldKeysForDebug
        {
            internal DateTime Created = DateTime.Now;
            internal TickCounter CreatedDelta = TickCounter.Now;
            internal BufLen Key;
            internal OldKeyTypes Type;
        }
        internal static ConcurrentDictionary<EndPoint, List<OldKeysForDebug>> 
            OldKeys = new ConcurrentDictionary<EndPoint, List<OldKeysForDebug>>();

        internal static void AddOldMacKey( EndPoint ep, BufLen key, OldKeyTypes type )
        {
            var newitem = new OldKeysForDebug
            {
                Key = key,
                Type = type
            };

            if ( OldKeys.TryGetValue( ep, out var list ) )
            {
                list.Add( newitem );
            }
            else
            {
                OldKeys[ep] = new List<OldKeysForDebug>( 
                    new OldKeysForDebug[] { newitem } );
            }
        }
#endif
#endif

        public const int SendQueueLengthUpperLimit = 1000;
        public const int ReceiveQueueLengthUpperLimit = 1000;

        public event Action<ITransport,Exception> ConnectionException;
        public event Action<ITransport> ConnectionShutDown;
        public event Action<ITransport,I2PIdentHash> ConnectionEstablished;
        public event Action<ITransport,II2NPHeader> DataBlockReceived;
        public event Action<ITransport,byte[]> TimeSyncReceived;

        internal SSUHost Host;
        internal IPEndPoint RemoteEP;
        internal I2PRouterAddress RemoteAddr;
        internal I2PKeysAndCert RemoteRouter;

        private BufLen IntroKeyField;
        internal BufLen RemoteIntroKey
        {
            get => IntroKeyField;
            set
            {
                IntroKeyField = value;
#if DEBUG
                Logging.LogTransport( $"SSUSession: {DebugId} {RemoteEP} IntroKey updated. {value}" );

                if ( RemoteEP is null ) return;

#if SSU_TRACK_OLD_MAC_KEYS
                AddOldMacKey( RemoteEP, value, OldKeyTypes.Intro );
#endif
#endif
            }
        }

        // RelayTag from SessionCreated
        internal BufLen RelayTag = null;

        // RemoteIntroducerInfo != null if the remote offered introduction
        internal IntroducerInfo RemoteIntroducerInfo = null;

        // True if we are firewalled and this is a current connection to
        // a introducer. Try to keep alive.
        internal bool IsIntroducerConnection = false;
        internal int RelayIntroductionsReceived = 0;

        // Network byte order
        internal uint SignOnTimeA;
        internal uint SignOnTimeB;

        private BufLen SharedKeyField;
        internal BufLen SharedKey 
        { 
            get => SharedKeyField;
            set
            {
                SharedKeyField = value;
#if DEBUG
                Logging.LogTransport( $"SSUSession: {DebugId} {RemoteEP} SharedKey updated. {value}" );

                if ( RemoteEP is null ) return;

#if SSU_TRACK_OLD_MAC_KEYS
                AddOldMacKey( RemoteEP, value, OldKeyTypes.Shared );
#endif
#endif
            }
        }

        private BufLen MACKeyField;
        internal BufLen MACKey 
        { 
            get => MACKeyField;
            set
            {
                MACKeyField = value;

#if DEBUG
                Logging.LogTransport( $"SSUSession: {DebugId} {RemoteEP} MACKey updated. {value}" );

                if ( RemoteEP is null ) return;

#if SSU_TRACK_OLD_MAC_KEYS
                AddOldMacKey( RemoteEP, value, OldKeyTypes.MAC );
#endif
#endif
            }
        }

        internal int TransportInstance;

        bool IsTerminated = false;

        public long BytesSent { get; protected set; }
        public long BytesReceived { get; protected set; }

        internal RouterContext MyRouterContext;
        IMTUProvider MTUProvider;
        internal MTUConfig MTU;

        internal readonly DataDefragmenter Defragmenter = new DataDefragmenter();
        internal readonly DataFragmenter Fragmenter = new DataFragmenter();

        // TODO: Make concurrent
        internal LinkedList<II2NPHeader5> SendQueue = new LinkedList<II2NPHeader5>();
        internal LinkedList<BufRefLen> ReceiveQueue = new LinkedList<BufRefLen>();

        internal TickCounter StartTime = TickCounter.Now;

        // We are client
        public SSUSession( 
                SSUHost owner, 
                IPEndPoint remoteep, 
                I2PRouterAddress remoteaddr, 
                I2PKeysAndCert rri, 
                IMTUProvider mtup, 
                RouterContext rc )
        {
            mOutgoing = true;
            Host = owner;
            RemoteEP = remoteep;
            RemoteAddr = remoteaddr;
            RemoteRouter = rri;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter );

            Logging.LogTransport( $"SSUSession: {DebugId} Client instance created." );

            if ( RemoteAddr == null ) throw new NullReferenceException( "SSUSession needs an address" );

            RemoteIntroKey = new BufLen( FreenetBase64.Decode( RemoteAddr.Options["key"] ) );
            MACKey = RouterContext.Inst.IntroKey; // TODO: Remove

            MTU = MTUProvider.GetMTU( remoteep );
        }

        // We are host
        public SSUSession( 
                SSUHost owner, 
                IPEndPoint remoteep, 
                IMTUProvider mtup, 
                RouterContext rc )
        {
            mOutgoing = false;
            Host = owner;
            RemoteEP = remoteep;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter ) + 10000;

            Logging.LogTransport( $"SSUSession: {DebugId} Host instance created." );

            MTU = MTUProvider.GetMTU( remoteep );

            SendQueue.AddLast( ( new DeliveryStatusMessage( (ulong)I2PConstants.I2P_NETWORK_ID ) ).Header5 );
            SendQueue.AddLast( ( new DatabaseStoreMessage( MyRouterContext.MyRouterInfo ) ).Header5 );

            CurrentState = new SessionCreatedState( this );
        }

        bool ConnectCalled = false;

        public string DebugId { get => $"+{TransportInstance}+"; }
        public string Protocol { get => "SSU"; }
        public bool Outgoing { get => mOutgoing; }
        private readonly bool mOutgoing;

        public void Connect()
        {
            // This instance was initiated as an incomming connection.
            // Do not change state as we might be in a handshake.
            if ( RemoteIntroKey == null ) return;

            lock ( this )
            {
                if ( ConnectCalled ) return;
                ConnectCalled = true;
            }

            if ( RemoteAddr.Options.Any( o => o.Key.ToString().StartsWith( "ihost", StringComparison.Ordinal ) ) )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: Trying introducers for " +
                    $"{RemoteAddr.Options.TryGet( "host" )?.ToString() ?? RemoteAddr.Options.ToString()}." );

                var introducers = GetIntroducers();

                var intros = new Dictionary<IntroducerInfo, SSUSession>();
                foreach( var i in introducers )
                {
                    Host.AccessSession( i.EndPoint, sess =>
                    {
                        if ( sess?.IsEstablished ?? false ) intros[i] = sess;
                    } );
                }

                CurrentState = new RelayRequestState( this, intros );
            }
            else
            {
                CurrentState = new SessionRequestState( this, false );
            }

            Host.NeedCpu( this );
        }

        private List<IntroducerInfo> GetIntroducers()
        {
            if ( !RemoteAddr.Options.Contains( "ihost0" ) || !RemoteAddr.Options.Contains( "iport0" ) || !RemoteAddr.Options.Contains( "ikey0" ) )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: No introducers declared." );
                throw new FailedToConnectException( "SSU Introducer required, but no introducer information available" );
            }

            var introducers = new List<IntroducerInfo>();

            for ( int i = 0; i < 3; ++i )
            {
                if ( !RemoteAddr.Options.Contains( $"ihost{i}" ) ) break;
                if ( !RemoteAddr.Options.Contains( $"iport{i}" ) ) break;
                if ( !RemoteAddr.Options.Contains( $"ikey{i}" ) ) break;
                if ( !RemoteAddr.Options.Contains( $"itag{i}" ) )
                {
                    Logging.LogWarning(
                      $"SSUSession: Connect {DebugId}: itag# not present! {RemoteAddr.Options}" );
                    break;
                }

                if ( !RemoteAddr.Options[$"ihost{i}"].Contains( '.' ) ) break;  // TODO: Support IPV6

                //Logging.LogWarning( $"SSUSession: Connect {DebugId}: {RemoteAddr.Options}" );

                var intro = new IntroducerInfo( RemoteAddr.Options[$"ihost{i}"],
                    RemoteAddr.Options[$"iport{i}"],
                    RemoteAddr.Options[$"ikey{i}"],
                    RemoteAddr.Options[$"itag{i}"] );

                Logging.LogTransport( $"SSUSession: Connect {DebugId}: " +
                    $"Adding introducer '{intro.EndPoint}'." );

                introducers.Add( intro );
            }

            if ( !introducers.Any() )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: Ended up with no introducers." );
                throw new FailedToConnectException( "SSU Introducer required, but no valid introducer information available" );
            }

            return introducers;
        }

#if DEBUG
        TickCounter MinTimeBetweenSendQueueLogs = new TickCounter();
        int SessionMaxSendQueueLength = 0;
#endif
        public void Send( I2NPMessage msg )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            lock ( SendQueue )
            {
                var len = SendQueue.Count;

                if ( len < SendQueueLengthUpperLimit )
                {
                    SendQueue.AddLast( msg.Header5 );
                }
#if DEBUG
                else
                {
                    Logging.LogWarning(
                        $"SSUSession {DebugId}: SendQueue is {len} messages long! " +
                        $"Dropping new message. Max queue: {SessionMaxSendQueueLength} " +
                        $"({MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds:###0}s)" );
                }

                SessionMaxSendQueueLength = Math.Max( SessionMaxSendQueueLength, len );

                if ( ( len > SendQueueLengthWarningLimit ) && ( MinTimeBetweenSendQueueLogs.DeltaToNowMilliseconds > 4000 ) ) 
                {
                    Logging.LogWarning(
                        $"SSUSession {DebugId}: SendQueue is {len} messages long! " +
                        $"Max queue: {SessionMaxSendQueueLength} ({MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds:###0}s)" );
                    MinTimeBetweenSendQueueLogs.SetNow();
                }
#endif
            }
        }

        public void Receive( BufRefLen recvbuf )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            int len;
            lock ( ReceiveQueue )
            {
                len = ReceiveQueue.Count;

                if ( len < ReceiveQueueLengthUpperLimit )
                {
                    ReceiveQueue.AddLast( recvbuf );
                    return;
                }
            }
#if DEBUG
            Logging.LogWarning(
                $"SSUSession {DebugId}: ReceiveQueue is {len} messages long! Dropping new message." );
#endif
        }

        public void Terminate()
        {
            IsTerminated = true;
        }

        public bool Terminated
        {
            get { return IsTerminated; }
        }

        public I2PKeysAndCert RemoteRouterIdentity
        {
            get { return RemoteRouter; }
        }

        public System.Net.IPAddress RemoteAddress
        {
            get {
                if ( RemoteEP == null ) return null;
                return RemoteEP.Address; 
            }
        }

        private SSUState CurrentStateField = null;
        private SSUState CurrentState
        {
            get => CurrentStateField;
            set
            {
                if ( CurrentStateField == value ) return;

                Logging.LogTransport( $"SSUSession {this} changed state from " +
                    $"{CurrentStateField?.GetType().Name ?? "<null>"} to {value?.GetType().Name ?? "<null>"}" );

                CurrentStateField = value;
            }
        }

        internal bool Run()
        {
            while ( ReceiveQueue.Count > 0 )
            {
                BufRefLen data;

                lock ( ReceiveQueue )
                {
                    data = ReceiveQueue.First.Value;
                    ReceiveQueue.RemoveFirst();
                }
                if ( !DatagramReceived( data, null ) ) return false;
            }

            return RunCurrentState( s => s.Run(), 3 );
        }

        bool RunCurrentState( 
                    Func<SSUState,SSUState> action,
                    int maxstatechanges )
        {
            SSUState cs;

            do
            {
                cs = CurrentState;
                if ( cs == null ) return false;

                try
                {
                    CurrentState = action( cs );

                    if ( CurrentState == null )
                    {
                        return SessionTerminated();
                    }
                }
                catch ( FailedToConnectException )
                {
                    Logging.LogTransport( $"SSUSession {DebugId}: RunCurrentState FailedToConnectException. Terminating." );
                    return SessionTerminated();
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                    return SessionTerminated();
                }
            } while ( CurrentState != null
                    && CurrentState != cs
                    && maxstatechanges-- > 0 );

            return true;
        }

        private bool SessionTerminated()
        {
            IsTerminated = true;
            CurrentState = null;
            Host.NoCpu( this );

            Logging.LogTransport( $"SSUSession {DebugId}: Shutting down." );
            ConnectionShutDown?.Invoke( this );

            if ( RemoteEP != null && IsIntroducerConnection )
            {
                if ( RelayIntroductionsReceived == 0 )
                {
                    Host.EPStatisitcs[RemoteEP].RelayIntrosReceived -= 5000;
                }

                Host.IntroducerSessionTerminated( this );

                IsIntroducerConnection = false;
            }

            return false;
        }

        internal bool IsEstablished
        {
            get
            {
                return CurrentState != null
                    && ( CurrentState is EstablishedState );
            }
        }

        internal bool DatagramReceived( BufRefLen recvbuf, IPEndPoint remoteep )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            Logging.LogDebugData( $"SSUSession {DebugId}: Received {recvbuf.Length} bytes [0x{recvbuf.Length:X}]." );

            return RunCurrentState( s => s.DatagramReceived( recvbuf, remoteep ), 1 );
        }

        internal void RaiseException( Exception ex )
        {
#if DEBUG
            if ( ConnectionException == null ) Logging.LogWarning( "SSUSession: " + DebugId + " No observers for ConnectionException!" );
#endif
            ConnectionException?.Invoke( this, ex );
        }

        internal void MessageReceived( II2NPHeader newmessage )
        {
#if DEBUG
            if ( DataBlockReceived == null ) Logging.LogWarning( $"SSUSession: {DebugId} No observers for DataBlockReceived!" );
#endif
            DataBlockReceived?.Invoke( this, newmessage );
        }

        internal void ReportConnectionEstablished()
        {
#if DEBUG
            if ( ConnectionEstablished == null ) Logging.LogWarning( $"SSUSession: {DebugId} No observers for ConnectionEstablished!" );
#endif
            ConnectionEstablished?.Invoke( this, RemoteRouter.IdentHash );
            Host.EPStatisitcs.UpdateConnectionTime( RemoteEP, StartTime.DeltaToNow );
            Host.EPStatisitcs.ConnectionSuccess( RemoteEP );
        }

        internal void SendDroppedMessageDetected()
        {
            MTU.MTU = Math.Max( MTU.MTUMin, MTU.MTU - 16 );
            MTUProvider.MTUUsed( RemoteEP, MTU );
        }

        internal PeerTest QueuedFirstPeerTestToCharlie = null;
        internal void SendFirstPeerTestToCharlie( PeerTest msg )
        {
            QueuedFirstPeerTestToCharlie = msg;
        }

        public override string ToString()
        {
            return $"'SSUSession: {DebugId} to {RemoteEP?.ToString() ?? "<null>"}'";
        }
    }
}
