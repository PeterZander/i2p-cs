using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using I2PCore.TransportLayer.NTCP;
using System.Threading;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.SessionLayer;
using I2PCore.TransportLayer.SSU.Data;
using System.Collections.Concurrent;

namespace I2PCore.TransportLayer.SSU
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

        private IPEndPoint RemoteEPField;
        internal IPEndPoint RemoteEP
        {
            get => RemoteEPField;
            set
            {
                if ( RemoteEPField != null )
                {
                    if ( RemoteEPField.Equals( value ) ) return;
                }

                RemoteEPField = RouterContext.UseIpV6 && value?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        ? new IPEndPoint( value.Address.MapToIPv6(), value.Port )
                        : value;

                Logging.LogTransport( $"SSUSession: {DebugId} RemoteEP updated: {RemoteEPField}" );

                var wasadded = Host.SessionEndPointUpdated( this, RemoteEPField );

                if ( value is null )
                {
                    Logging.LogTransport( $"SSUSession: {DebugId} RemoteEP is null" );
                    return;
                }

                if ( !wasadded )
                        throw new EndOfStreamEncounteredException( $"SSU {this}: Endpoint session already running" );

                if ( MTU == -1 )
                {
                    MTU = UnwrappedRemoteAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            ? RouterContext.IPV4MTU
                            : RouterContext.IPV6MTU;
                }
            }
        }

        protected readonly Action<IPEndPoint, BufLen> Sendmethod;

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

        public long BytesSent { get => BWStat.SendBandwidth.DataBytes; }
        public long BytesReceived { get => BWStat.ReceiveBandwidth.DataBytes; }

        private readonly BandwidthStatistics BWStat = new BandwidthStatistics();

        internal RouterContext MyRouterContext;

        internal int MTU = -1;

        internal readonly DataDefragmenter Defragmenter = new DataDefragmenter();
        internal DataFragmenter Fragmenter = new DataFragmenter();

        internal ConcurrentQueue<II2NPHeader16> SendQueue = new ConcurrentQueue<II2NPHeader16>();
        internal ConcurrentQueue<BufRefLen> ReceiveQueue = new ConcurrentQueue<BufRefLen>();

        internal TickCounter StartTime = TickCounter.Now;

        // We are client
        public SSUSession( 
                SSUHost owner,
                Action<IPEndPoint, BufLen> sendmethod,
                I2PRouterInfo router,
                RouterContext rc )
        {
            IsOutgoing = true;
            Host = owner;
            Sendmethod = sendmethod;
            RemoteRouterInfo = router;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter );

            Logging.LogTransport( $"SSUSession: {DebugId} Client instance created." );

            MACKey = RouterContext.Inst.IntroKey; // TODO: Remove
            CurrentState = new IdleState( this );
        }

        // We are host
        public SSUSession( 
                SSUHost owner,
                Action<IPEndPoint, BufLen> sendmethod,
                IPEndPoint remoteep, 
                RouterContext rc )
        {
            IsOutgoing = false;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter ) + 10000;
            Host = owner;
            Sendmethod = sendmethod;
            RemoteEP = remoteep;
            MyRouterContext = rc;

            Logging.LogTransport( $"SSUSession: {DebugId} Host instance created." );

            SendQueue.Enqueue( ( new DeliveryStatusMessage( (ulong)I2PConstants.I2P_NETWORK_ID ) ).CreateHeader16 );
            SendQueue.Enqueue( ( new DatabaseStoreMessage( MyRouterContext.MyRouterInfo ) ).CreateHeader16 );

            CurrentState = new SessionCreatedState( this );
        }

        bool ConnectCalled = false;

        public string DebugId { get => $"+{TransportInstance}+"; }
        public string Protocol { get => "SSU"; }
        public bool IsOutgoing { get; private set; }

        ~SSUSession()
        {
        }

        public void Connect()
        {
            // This instance was initiated as an incomming connection.
            // Do not change state as we might be in a handshake.
            if ( !IsOutgoing ) return;

            lock ( this )
            {
                if ( ConnectCalled ) return;
                ConnectCalled = true;
            }

            var remoteaddr = Host.SelectAddress( RemoteRouterInfo );
            if ( remoteaddr == null ) throw new NullReferenceException( $"SSUSession {this} needs an address" );
            RemoteIntroKey = new BufLen( FreenetBase64.Decode( remoteaddr.Options["key"] ) );

            if ( remoteaddr.Options.Any( o => o.Key.ToString().StartsWith( "ihost", StringComparison.Ordinal ) ) )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: Trying introducers for " +
                    $"{remoteaddr.Options.TryGet( "host" )?.ToString() ?? remoteaddr.Options.ToString()}." );

                var introducers = GetIntroducers( remoteaddr );

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
                RemoteEP = new IPEndPoint( remoteaddr.Host, remoteaddr.Port );
                CurrentState = new SessionRequestState( this, false );
            }

            Host.NeedCpu( this );
        }

        private List<IntroducerInfo> GetIntroducers( I2PRouterAddress remoteaddr )
        {
            if ( !remoteaddr.Options.Contains( "ihost0" ) || !remoteaddr.Options.Contains( "iport0" ) || !remoteaddr.Options.Contains( "ikey0" ) )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: No introducers declared." );
                throw new FailedToConnectException( "SSU Introducer required, but no introducer information available" );
            }

            var introducers = new List<IntroducerInfo>();

            for ( int i = 0; i < 3; ++i )
            {
                if ( !remoteaddr.Options.Contains( $"ihost{i}" ) ) break;
                if ( !remoteaddr.Options.Contains( $"iport{i}" ) ) break;
                if ( !remoteaddr.Options.Contains( $"ikey{i}" ) ) break;
                if ( !remoteaddr.Options.Contains( $"itag{i}" ) )
                {
                    Logging.LogWarning(
                      $"SSUSession: Connect {DebugId}: itag# not present! {remoteaddr.Options}" );
                    break;
                }

                if ( !remoteaddr.Options[$"ihost{i}"].Contains( '.' ) ) break;  // TODO: Support IPV6

                //Logging.LogWarning( $"SSUSession: Connect {DebugId}: {RemoteAddr.Options}" );

                var intro = new IntroducerInfo( remoteaddr.Options[$"ihost{i}"],
                    remoteaddr.Options[$"iport{i}"],
                    remoteaddr.Options[$"ikey{i}"],
                    remoteaddr.Options[$"itag{i}"] );

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
            if ( IsTerminated ) throw new EndOfStreamEncounteredException();

            var len = SendQueue.Count;

            if ( len < SendQueueLengthUpperLimit )
            {
                SendQueue.Enqueue( msg.CreateHeader16 );
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

        internal void Send( IPEndPoint ep, BufLen data )
        {
            BWStat.DataSent( data.Length );
            Sendmethod( ep, data );
        }

        public void Receive( BufRefLen recvbuf )
        {
            if ( IsTerminated ) throw new EndOfStreamEncounteredException();

            BWStat.DataReceived( recvbuf.Length );

            var len = ReceiveQueue.Count;
            if ( len < ReceiveQueueLengthUpperLimit )
            {
                ReceiveQueue.Enqueue( recvbuf );
                return;
            }
#if DEBUG
            Logging.LogWarning(
                $"SSUSession {DebugId}: ReceiveQueue is {len} messages long! Dropping new message." );
#endif
        }

        public bool IsTerminated { get; protected set; }

        public I2PKeysAndCert RemoteRouterIdentity
        {
            get { return RemoteRouterReceivedIdentity ?? RemoteRouterInfo?.Identity; }
        }

        internal I2PRouterInfo RemoteRouterInfo;
        internal I2PCertificate RemoteCert { get => RemoteRouterIdentity?.Certificate; }

        /// <summary>
        /// From received SessionConfirmed
        /// </summary>
        internal I2PRouterIdentity RemoteRouterReceivedIdentity;

        public System.Net.IPAddress RemoteAddress
        {
            get
            {
                return RemoteEP?.Address; 
            }
        }

        public System.Net.IPAddress UnwrappedRemoteAddress
        {
            get
            {
                if ( RemoteEP == null ) return null;
                return RemoteEP.Address.IsIPv4MappedToIPv6
                        ? RemoteEP.Address.MapToIPv4()
                        : RemoteEP.Address;
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
            while ( !ReceiveQueue.IsEmpty && !IsTerminated )
            {
                if ( !ReceiveQueue.TryDequeue( out var data ) )
                {
                    continue;
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
                if ( cs == null || IsTerminated ) return false;

                try
                {
                    CurrentState = action( cs );

                    if ( CurrentState == null )
                    {
                        Terminate();
                        return false;
                    }
                }
                catch ( FailedToConnectException )
                {
                    Logging.LogTransport( $"SSUSession {DebugId}: RunCurrentState FailedToConnectException. Terminating." );
                    Terminate();
                    return false;
                }
                catch ( SignatureCheckFailureException )
                {
                    Logging.LogTransport( $"SSUSession {DebugId}: RunCurrentState SignatureCheckFailureException. Terminating." );
                    Terminate();
                    return false;
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug( ex );
                    Terminate();
                    return false;
                }
            } while ( CurrentState != null
                    && CurrentState != cs
                    && !IsTerminated
                    && maxstatechanges-- > 0 );

            return true;
        }

        public void Terminate()
        {
            if ( IsTerminated ) return;

            CurrentState = null;
            Host.NoCpu( this );
            IsTerminated = true;

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
            if ( IsTerminated ) throw new EndOfStreamEncounteredException();

            Logging.LogDebugData( $"SSUSession {DebugId}: Received {recvbuf.Length} bytes [0x{recvbuf.Length:X}]." );

            return RunCurrentState( s => s.DatagramReceived( recvbuf, remoteep ), 1 );
        }

        internal void RaiseException( Exception ex )
        {
#if DEBUG
            if ( ConnectionException == null )
                    Logging.LogWarning( $"SSUSession: {DebugId} No observers for ConnectionException!" );
#endif
            ConnectionException?.Invoke( this, ex );
        }

        internal void MessageReceived( II2NPHeader newmessage )
        {
#if DEBUG
            if ( DataBlockReceived == null )
                    Logging.LogWarning( $"SSUSession: {DebugId} No observers for DataBlockReceived!" );
#endif
            DataBlockReceived?.Invoke( this, newmessage );
        }

        internal void ReportConnectionEstablished()
        {
#if DEBUG
            if ( ConnectionEstablished == null )
                    Logging.LogWarning( $"SSUSession: {DebugId} No observers for ConnectionEstablished!" );
#endif
            ConnectionEstablished?.Invoke( this, RemoteRouterIdentity.IdentHash );
            Host.EPStatisitcs.UpdateConnectionTime( RemoteEP, StartTime.DeltaToNow );
            Host.EPStatisitcs.ConnectionSuccess( RemoteEP );
        }

        internal object RunLock = new object();

        public void DatabaseStoreMessageReceived( DatabaseStoreMessage dsm )
        {
            try
            {
                var remoteaddr = UnwrappedRemoteAddress;

                var ssuaddr = dsm.RouterInfo.Adresses
                        .Where( a => a.TransportStyle == "SSU"
                                && a.HaveHostAndPort
                                && a.Options.Contains( "mtu" )
                                && remoteaddr.Equals( a.Host ) )
                        .Select( a => a.Options["mtu"] );

                if ( !ssuaddr.Any() ) return;

                if ( int.TryParse( ssuaddr.FirstOrDefault(), out var mtu ) )
                {
                    if ( mtu < MTU )
                    {
                        MTU = mtu;

                        // The FragmentedMessages wont fit in a buffer anymore.
                        Fragmenter = new DataFragmenter();

                        Logging.LogTransport( () => $"SSUSession: {DebugId} reducing MTU to {mtu} for {dsm.RouterInfo}" );
                    }
                }
            }
            catch( Exception ex )
            {
                Logging.LogDebug( ToString(), ex );
            }
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
