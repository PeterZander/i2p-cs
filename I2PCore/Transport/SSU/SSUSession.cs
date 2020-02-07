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

namespace I2PCore.Transport.SSU
{
    public class SSUSession: ITransport
    {
#if DEBUG
        public const int SendQueueLengthWarningLimit = 50;
#endif
        public const int SendQueueLengthUpperLimit = 1000;
        public const int ReceiveQueueLengthUpperLimit = 1000;

        public event Action<ITransport, Exception> ConnectionException;
        public event Action<ITransport> ConnectionShutDown;
        public event Action<ITransport> ConnectionEstablished;
        public event Action<ITransport, II2NPHeader> DataBlockReceived;
        public event Action<ITransport, byte[]> TimeSyncReceived;

        internal SSUHost Host;
        internal IPEndPoint RemoteEP;
        internal I2PRouterAddress RemoteAddr;
        internal I2PKeysAndCert RemoteRouter;
        internal BufLen IntroKey;

        // RelayTag from SessionCreated
        internal BufLen RelayTag = null;

        // RemoteIntroducerInfo != null if the remote offered introduction
        internal IntroducerInfo RemoteIntroducerInfo = null;

        // True if we are firewalled and this is a current connection to
        // a introducer. Try to keep alive.
        internal bool IsIntroducerConnection = false;

        // Network byte order
        internal uint SignOnTimeA;
        internal uint SignOnTimeB;
        internal BufLen SharedKey;
        internal BufLen MACKey;

        internal int TransportInstance;

        bool IsTerminated = false;

        public long BytesSent { get; protected set; }
        public long BytesReceived { get; protected set; }

        internal RouterContext MyRouterContext;
        IMTUProvider MTUProvider;
        internal MTUConfig MTU;

        internal readonly DataDefragmenter Defragmenter = new DataDefragmenter();
        internal readonly DataFragmenter Fragmenter = new DataFragmenter();

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
            Host = owner;
            RemoteEP = remoteep;
            RemoteAddr = remoteaddr;
            RemoteRouter = rri;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter );

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( $"SSUSession: {DebugId} Client instance created." );
#endif

            if ( RemoteAddr == null ) throw new NullReferenceException( "SSUSession needs an address" );

            IntroKey = new BufLen( FreenetBase64.Decode( RemoteAddr.Options["key"] ) );

            MTU = MTUProvider.GetMTU( remoteep );
        }

        // We are host
        public SSUSession( 
                SSUHost owner, 
                IPEndPoint remoteep, 
                IMTUProvider mtup, 
                RouterContext rc )
        {
            Host = owner;
            RemoteEP = remoteep;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter ) + 10000;

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( "SSUSession: " + DebugId + " Host instance created." );
#endif

            MTU = MTUProvider.GetMTU( remoteep );

            SendQueue.AddLast( ( new DeliveryStatusMessage( (ulong)I2PConstants.I2P_NETWORK_ID ) ).Header5 );
            SendQueue.AddLast( ( new DatabaseStoreMessage( MyRouterContext.MyRouterInfo ) ).Header5 );

            CurrentState = new SessionCreatedState( this );
        }

        bool ConnectCalled = false;

        public string DebugId { get { return $"+{TransportInstance}+"; } }

        public void Connect()
        {
            // This instance was initiated as an incomming connection.
            // Do not change state as we might be in a handshake.
            if ( IntroKey == null ) return;

            if ( ConnectCalled ) return;
            ConnectCalled = true;

            if ( RemoteAddr.Options.Any( o => o.Key.ToString().StartsWith( "ihost", StringComparison.Ordinal ) ) )
            {
                Logging.LogTransport( $"SSUSession: Connect {DebugId}: Trying introducers for " +
                    $"{RemoteAddr.Options.TryGet( "host" )?.ToString() ?? RemoteAddr.Options.ToString()}." );

                var introducers = GetIntroducers();

                var intros = new Dictionary<IntroducerInfo, SSUSession>();
                foreach( var i in introducers )
                {
                    Host.AccessSession( i.EndPoint, sess => intros[i] = sess );
                }

                CurrentState = new RelayRequestState( this, intros );
            }
            else
            {
                CurrentState = new SessionRequestState( this );
            }

            Host.NeedCpu( this );
        }

        private List<IntroducerInfo> GetIntroducers()
        {
            if ( !RemoteAddr.Options.Contains( "ihost0" ) || !RemoteAddr.Options.Contains( "iport0" ) || !RemoteAddr.Options.Contains( "ikey0" ) )
            {
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( $"SSUSession: Connect {DebugId}: No introducers declared." );
#endif
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

#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( $"SSUSession: Connect {DebugId}: " +
                        $"Adding introducer '{intro.EndPoint}'." );
#endif
                introducers.Add( intro );
            }

            if ( !introducers.Any() )
            {
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( $"SSUSession: Connect {DebugId}: Ended up with no introducers." );
#endif
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

        SSUState CurrentState = null;
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
                DatagramReceived( data, null );
            }

            var cs = CurrentState;
            if ( cs == null )
            {
                IsTerminated = true;
                return false;
            }

            // TODO: Handle exceptions and make sure ConnectionShutDown gets called.
            CurrentState = cs.Run();

            if ( CurrentState != null && cs != CurrentState )
            {
                CurrentState = CurrentState.Run();
            }

            if ( CurrentState == null )
            {
                Logging.LogTransport( $"SSUSession {DebugId}: Shuting down. No state." );
                Host.NoCpu( this );
                ConnectionShutDown?.Invoke( this );
                IsTerminated = true;
                return false;
            }
            return true;
        }

        internal void DatagramReceived( BufRefLen recvbuf, IPEndPoint remoteep )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

#if LOG_ALL_TRANSPORT
            Logging.LogTransport( string.Format( "SSUSession {0}: Received {1} bytes [0x{1:X}].", DebugId, recvbuf.Length ) );
#endif

            var cs = CurrentState;
            if ( cs != null ) CurrentState = cs.DatagramReceived( recvbuf, remoteep );
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
            ConnectionEstablished?.Invoke( this );
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
