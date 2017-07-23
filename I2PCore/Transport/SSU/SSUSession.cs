using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using I2PCore.Utils;
using I2PCore.Transport.NTCP;
using System.Threading;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto;
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

        internal BufLen RelayTag = null;

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

        internal DataDefragmenter Defragmenter = new DataDefragmenter();
        internal DataFragmenter Fragmenter;

        internal LinkedList<II2NPHeader5> SendQueue = new LinkedList<II2NPHeader5>();
        internal LinkedList<BufRefLen> ReceiveQueue = new LinkedList<BufRefLen>();

        // We are client
        public SSUSession( SSUHost owner, IPEndPoint remoteep, I2PRouterAddress remoteaddr, I2PKeysAndCert rri, IMTUProvider mtup, RouterContext rc )
        {
            Host = owner;
            RemoteEP = remoteep;
            RemoteAddr = remoteaddr;
            RemoteRouter = rri;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter );

#if LOG_ALL_TRANSPORT
            DebugUtils.LogDebug( "SSUSession: " + DebugId + " Client instance created." );
#endif

            Fragmenter = new DataFragmenter();

            if ( RemoteAddr == null ) throw new NullReferenceException( "SSUSession needs an address" );

            IntroKey = new BufLen( FreenetBase64.Decode( RemoteAddr.Options["key"] ) );

            MTU = MTUProvider.GetMTU( remoteep );
        }

        // Session to introducer
        internal SSUSession( SSUHost owner, IPEndPoint remoteep, IntroducerInfo ii, IMTUProvider mtup, RouterContext rc )
        {
            Host = owner;
            RemoteEP = remoteep;
            MTUProvider = mtup;
            MyRouterContext = rc;

            RemoteAddr = new I2PRouterAddress( ii.Host, ii.Port, 0, "SSU" );

            // TODO: This is what PurpleI2P does. Seems strange... But there is no RouterInfo available for introducer sessions.
            RemoteRouter = MyRouterContext.MyRouterIdentity;

            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter );

#if LOG_ALL_TRANSPORT
            DebugUtils.LogDebug( "SSUSession: " + DebugId + " Introducer instance created." );
#endif

            Fragmenter = new DataFragmenter();

            if ( RemoteAddr == null ) throw new NullReferenceException( "SSUSession needs an address" );

            IntroKey = ii.IntroKey;

            MTU = MTUProvider.GetMTU( remoteep );
        }

        // We are host
        public SSUSession( SSUHost owner, IPEndPoint remoteep, IMTUProvider mtup, RouterContext rc )
        {
            Host = owner;
            RemoteEP = remoteep;
            MTUProvider = mtup;
            MyRouterContext = rc;
            TransportInstance = Interlocked.Increment( ref NTCPClient.TransportInstanceCounter ) + 10000;

#if LOG_ALL_TRANSPORT
            DebugUtils.LogDebug( "SSUSession: " + DebugId + " Host instance created." );
#endif

            Fragmenter = new DataFragmenter();

            MTU = MTUProvider.GetMTU( remoteep );

            SendQueue.AddLast( ( new DeliveryStatusMessage( (ulong)I2PConstants.I2P_NETWORK_ID ) ).Header5 );
            SendQueue.AddLast( ( new DatabaseStoreMessage( MyRouterContext.MyRouterInfo ) ).Header5 );

            CurrentState = new SessionCreatedState( this );
        }

        bool ConnectCalled = false;

        public string DebugId { get { return "+" + TransportInstance.ToString() + "+"; } }

        public void Connect()
        {
            // This instance was initiated as an incomming connection.
            // Do not change state as we might be in a handshake.
            if ( IntroKey == null ) return;

            if ( ConnectCalled ) return;
            ConnectCalled = true;

            // TODO: Add introducer request
            if ( !RemoteAddr.Options.Contains( "host" ) || !RemoteAddr.Options.Contains( "port" ) )
            {
#if LOG_ALL_TRANSPORT
                DebugUtils.LogDebug( "SSUSession: Connect " + DebugId + ": No host info. Trying introducers." );
#endif
                if ( !RemoteAddr.Options.Contains( "ihost0" ) || !RemoteAddr.Options.Contains( "iport0" ) || !RemoteAddr.Options.Contains( "ikey0" ) )
                {
#if LOG_ALL_TRANSPORT
                    DebugUtils.LogDebug( "SSUSession: Connect +" + TransportInstance.ToString() + "+: No introducers declared." );
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
                        DebugUtils.LogWarning(
                          $"SSUSession: Connect +{TransportInstance}+: itag# not present! {RemoteAddr.Options}" );
                        break;
                    }

                    if ( !RemoteAddr.Options[$"ihost{i}"].Contains( '.' ) ) break;  // TODO: Support IPV6

                    //DebugUtils.LogWarning( "SSUSession: Connect " + DebugId + ": " + RemoteAddr.Options.ToString() );

                    var intro = new IntroducerInfo( RemoteAddr.Options[$"ihost{i}"],
                        RemoteAddr.Options[$"iport{i}"],
                        RemoteAddr.Options[$"ikey{i}"],
                        RemoteAddr.Options[$"itag{i}"] );

#if LOG_ALL_TRANSPORT
                    DebugUtils.LogDebug( "SSUSession: Connect +" + TransportInstance.ToString() + "+: Adding introducer '" +
                        intro.EndPoint.ToString() + "'." );
#endif
                    introducers.Add( intro );
                }

                if ( introducers.Count == 0 )
                {
#if LOG_ALL_TRANSPORT
                    DebugUtils.LogDebug( "SSUSession: Connect +" + TransportInstance.ToString() + "+: Ended up with no introducers." );
#endif
                    throw new FailedToConnectException( "SSU Introducer required, but no valid introducer information available" );
                }

                CurrentState = new RelayRequestState( this, introducers );
            }
            else
            {
                CurrentState = new SessionRequestState( this );
            }

            Host.NeedCpu( this );
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

                if ( len < SendQueueLengthUpperLimit ) SendQueue.AddLast( msg.Header5 );
#if DEBUG
                else
                {
                    DebugUtils.LogWarning(
                        string.Format( "SSUSession {0}: SendQueue is {1} messages long! Dropping new message. Max queue: {2} ({3:###0}s)",
                        DebugId, len, SessionMaxSendQueueLength, MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds ) );
                }

                SessionMaxSendQueueLength = Math.Max( SessionMaxSendQueueLength, len );

                if ( ( len > SendQueueLengthWarningLimit ) && ( MinTimeBetweenSendQueueLogs.DeltaToNowMilliseconds > 4000 ) ) 
                {
                    DebugUtils.LogWarning( 
                        string.Format( "SSUSession {0}: SendQueue is {1} messages long! Max queue: {2} ({3:###0}s)",
                        DebugId, len, SessionMaxSendQueueLength, MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds ) );
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
            DebugUtils.LogWarning(
                string.Format( "SSUSession {0}: ReceiveQueue is {1} messages long! Dropping new message.",
                DebugId, len ) );
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
                DebugUtils.Log( string.Format( "SSUSession {0}: Shuting down. No state.", DebugId ) );
                Host.NoCpu( this );
                if ( ConnectionShutDown != null ) ConnectionShutDown( this );
                IsTerminated = true;
                return false;
            }
            return true;
        }

        internal void DatagramReceived( BufRefLen recvbuf, IPEndPoint remoteep )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

#if LOG_ALL_TRANSPORT
            DebugUtils.Log( string.Format( "SSUSession +{0}+: Received {1} bytes [0x{1:X}].", TransportInstance, recvbuf.Length ) );
#endif

            var cs = CurrentState;
            if ( cs != null ) CurrentState = cs.DatagramReceived( recvbuf, remoteep );
        }

        internal void RaiseException( Exception ex )
        {
#if DEBUG
            if ( ConnectionException == null ) DebugUtils.LogWarning( "SSUSession: " + DebugId + " No observers for ConnectionException!" );
#endif
            if ( ConnectionException != null ) ConnectionException( this, ex );
        }

        internal void MessageReceived( II2NPHeader newmessage )
        {
#if DEBUG
            if ( DataBlockReceived == null ) DebugUtils.LogWarning( "SSUSession: " + DebugId + " No observers for DataBlockReceived!" );
#endif
            if ( DataBlockReceived != null ) DataBlockReceived( this, newmessage );
        }

        internal void ReportConnectionEstablished()
        {
#if DEBUG
            if ( ConnectionEstablished == null ) DebugUtils.LogWarning( "SSUSession: " + DebugId + " No observers for ConnectionEstablished!" );
#endif
            if ( ConnectionEstablished != null ) ConnectionEstablished( this );
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
            return "'SSUSession: " + DebugId + " to " + ( RemoteEP == null ? "<null>" : RemoteEP.ToString() ) + "'";
        }
    }
}
