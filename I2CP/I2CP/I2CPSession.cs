using System;
using System.Collections.Generic;
using System.Linq;
using I2P.I2CP.Messages;
using I2PCore.Data;
using I2PCore.Utils;
using System.Net.Sockets;
using I2P.I2CP.States;
using System.Threading.Tasks;
using I2PCore;
using System.Collections.Concurrent;
using static I2P.I2CP.Messages.I2CPMessage;
using I2PCore.SessionLayer;
using static I2PCore.SessionLayer.ClientDestination;
using System.Runtime.CompilerServices;
using System.Threading;

namespace I2P.I2CP
{
    public class SessionInfo
    {
        public SessionInfo( ushort id ) { SessionId = id; }

        public ushort SessionId { get; private set; }

        private uint MessageIdField = 0;
        public uint MessageId { get => ++MessageIdField; }

        public I2PSessionConfig Config { get; set; }

        public ClientDestination MyDestination { get; internal set; }
    }

    public class I2CPSession
    {
        internal readonly I2CPHost Host;
        internal TcpClient MyTcpClient;

        public ushort SessionId = 1;
        public bool Terminated { get; private set; }

        static int InstanceCounter;
        readonly string InstanceInfo;
        public string DebugId { get { return $"--{InstanceInfo}:{SessionId}--"; } }

        internal ConcurrentDictionary<ushort, SessionInfo> SessionIds =
            new ConcurrentDictionary<ushort, SessionInfo>();

        internal I2CPState CurrentState;
        private NetworkStream MyStream;

        internal TickCounter LastReception = TickCounter.Now;

        readonly byte[] RecvBuf = new byte[65536];

        // We are host
        public I2CPSession( I2CPHost host, TcpClient client )
        {
            Host = host;
            MyTcpClient = client;

            CurrentState = new WaitGetDateState( this );
            MyStream = MyTcpClient.GetStream();

            InstanceInfo = $"{++InstanceCounter}";
        }

        readonly CancellationTokenSource CTSource = new CancellationTokenSource();

        internal async Task Run()
        {
            try
            {
                var recvbuf = new BufLen( RecvBuf );

                var readlen = await MyStream.ReadAsync( RecvBuf, 0, 1, CTSource.Token ).ConfigureAwait( false );
                if ( readlen != 1 )
                {
                    throw new FailedToConnectException( $"I2CPSession. Failed to read protocol version" );
                }

                if ( RecvBuf[0] != I2PConstants.PROTOCOL_BYTE )
                {
                    throw new FailedToConnectException( $"I2CPSession. Wrong protocol version {RecvBuf[0]}" );
                }

                while ( !( CurrentState is null ) )
                {
                    readlen = await MyStream.ReadAsync( RecvBuf, 0, 5, CTSource.Token ).ConfigureAwait( false );
                    if ( readlen != 5 )
                    {
                        Logging.LogDebug( $"{this}: Failed to read message header 5. Got {readlen} bytes." );
                        break;
                    }

                    var msglen = recvbuf.PeekFlip32( 0 );
                    var msgtype = recvbuf[4];

                    if ( msglen + 5 >= RecvBuf.Length )
                    {
                        Logging.LogWarning( $"{this}: Failed to read message {msglen}. Receivebuffer too small. Quitting." );
                        break;
                    }

                    if ( msglen == 0 ) continue;

                    var startpos = 5;
                    var toread = (int)msglen;
                again:
                    readlen = await MyStream.ReadAsync( RecvBuf, startpos, toread, CTSource.Token ).ConfigureAwait( false );

                    if ( readlen == 0 )
                    {
                        Logging.LogInformation( $"{this}: Failed to read message {msglen}. Got end of stream." );
                        break;
                    }

                    if ( readlen != msglen )
                    {
                        Logging.LogDebug( $"{this}: Failed to read message {msglen}. Got {readlen} bytes." );

                        startpos += readlen;
                        toread -= readlen;

                        goto again;
                    }

                    LastReception.SetNow();

                    try
                    {
                        var msg = GetMessage(
                                new BufRefLen( recvbuf, 0, readlen + 5 ).Clone() );

                        var nextstate = CurrentState.MessageReceived( msg );
                        if ( nextstate != CurrentState )
                        {
                            Logging.LogDebug( $"{this}: Changed state from {CurrentState} to {nextstate}" );
                            CurrentState = nextstate;
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.LogWarning( $"{this} {ex}" );
                    }
                }
            }
            catch ( Exception ex )
            {
                Logging.LogWarning( $"{this} {ex}" );
            }
            finally
            {
                Terminate();

                foreach ( var session in SessionIds )
                {
                    DetachDestination( session.Value.MyDestination );
                    session.Value.MyDestination?.Shutdown();
                }

                Logging.LogInformation( $"{this}: Session closed." );
            }
        }

        internal void AttachDestination( ClientDestination dest )
        {
            dest.DataReceived += MyDestination_DataReceived;
            dest.SignLeasesRequest += MyDestination_SignLeasesRequest;
            dest.ClientStateChanged += MyDestination_ClientStateChanged;
        }

        internal void DetachDestination( ClientDestination dest )
        {
            dest.DataReceived -= MyDestination_DataReceived;
            dest.SignLeasesRequest -= MyDestination_SignLeasesRequest;
            dest.ClientStateChanged -= MyDestination_ClientStateChanged;
        }

        internal void Terminate( [CallerMemberName] string caller = "" )
        {
            try
            {
                CurrentState = null;

                if ( Terminated ) return;

                CTSource?.Cancel( false );

                Logging.LogInformation( $"{this}: Terminating {DebugId} from {MyTcpClient.Client.RemoteEndPoint} by {caller}." );

                try
                {
                    MyTcpClient?.Close();
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug( ex );
                }

                try
                {
                    MyTcpClient?.Dispose();
                    MyTcpClient = null;
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug( ex );
                }

                foreach ( var destsid in SessionIds )
                {
                    try
                    {
                        var dest = destsid.Value.MyDestination;

                        if ( dest != null )
                        {
                            DetachDestination( dest );
                            dest?.Shutdown();
                        }
                    }
                    catch ( Exception ex )
                    {
                        Logging.LogDebug( ex );
                    }
                }

                Terminated = true;
            }
            catch ( Exception ex )
            {
                Logging.LogDebug( ex );
            }
        }

        ushort PrevSessionId = 0;
        internal SessionInfo GenerateNewSessionId()
        {
            var newid = ++PrevSessionId;
            if ( PrevSessionId > 30000 ) PrevSessionId = 0;

            var result = new SessionInfo( newid );

            SessionIds[newid] = result;
            return result;
        }

        readonly ConcurrentQueue<I2CPMessage> SendQueue = new ConcurrentQueue<I2CPMessage>();
        bool SendInProgress = false;

        internal void Send( I2CPMessage msg )
        {
            lock ( SendQueue )
            {
                if ( SendInProgress )
                {
                    SendQueue.Enqueue( msg );
                    return;
                }

                SendInProgress = true;
                SendOneMessage( msg );
            }
        }

        private void SendOneMessage( I2CPMessage msg )
        {
            try
            {
                var header = new byte[5];
                var writer = new BufRefLen( header );
                var data = msg.ToByteArray();
                writer.WriteFlip32( (uint)data.Length );
                writer.Write8( (byte)msg.MessageType );

                Logging.LogDebug( $"{this} SendOneMessage: {msg.MessageType} {new BufLen( header ):h} {new BufLen( data ):20}" );

                MyTcpClient.Client.BeginSend(
                    new List<ArraySegment<byte>> {
                        new ArraySegment<byte>( header ),
                        new ArraySegment<byte>( data )
                    },
                    SocketFlags.None,
                    ( ar ) =>
                    {
                        try
                        {
                            MyTcpClient?.Client?.EndSend( ar );

                            lock ( SendQueue )
                            {
                                if ( SendQueue.IsEmpty || !SendQueue.TryDequeue( out var newmsg ) )
                                {
                                    SendInProgress = false;
                                    return;
                                }

                                SendOneMessage( newmsg );
                            }
                        }
                        catch ( Exception ex )
                        {
                            Logging.LogDebug( ex );
                            Terminate();
                        }
                    },
                    this );
            }
            catch ( Exception ex )
            {
                Logging.LogDebug( $"{this} {ex}" );
                Terminate();
            }
        }

        internal void MyDestination_ClientStateChanged( ClientDestination dest, ClientStates state )
        {
            if ( Terminated ) return;

            var sessid = SessionIds
                    .FirstOrDefault( s => s.Value.MyDestination == dest );
            if ( Equals( sessid, default( KeyValuePair<ushort, SessionInfo> ) ) ) return;

            Logging.LogDebug( $"{this} MyDestination_ClientStateChanged: {sessid.Key}, {state}" );

            if ( sessid.Value.MyDestination.Terminated )
            {
                Terminate();
                return;
            }
        }

        SessionInfo FindSession( ClientDestination dest, [CallerMemberName] string caller = "NA" )
        {
            var sessid = SessionIds
                    .SingleOrDefault( s => s.Value.MyDestination == dest );
            if ( Equals( sessid, default( KeyValuePair<ushort, SessionInfo> ) ) )
            {
                Logging.LogWarning( $"{this} FindSession {caller}: Cannot find session id of {dest}" );
                return null;
            }

            return sessid.Value;
        }

        SessionInfo FindSession( I2PDestination dest, [CallerMemberName] string caller = "NA" )
        {
            var sessid = SessionIds
                    .SingleOrDefault( s => s.Value.MyDestination.Destination.IdentHash == dest.IdentHash );
            if ( Equals( sessid, default( KeyValuePair<ushort, SessionInfo> ) ) )
            {
                Logging.LogWarning( $"{this} FindSession {caller}: Cannot find session id of destination {dest}" );
                return null;
            }

            return sessid.Value;
        }

        internal void MyDestination_DataReceived( ClientDestination dest, BufLen data )
        {
            if ( Terminated ) return;

            var ldata = data;
            var sessid = FindSession( dest );

            Logging.LogDebugData( $"{this} MyDestination_DataReceived: Received message {sessid.SessionId} {dest} {(PayloadFormat)ldata[9]}, {ldata}" );

            if ( sessid.MyDestination.Terminated )
            {
                Terminate();
                return;
            }

            Send( new MessagePayloadMessage(
                    sessid.SessionId,
                    sessid.MessageId,
                    ldata ) );
        }

        readonly PendingLeaseUpdateInfo PendingLeaseUpdate = new PendingLeaseUpdateInfo();

        class PendingLeaseUpdateInfo
        {
            public TickCounter LockedAt = TickCounter.Now;
            private bool UpdateInProgressField = false;
            public bool UpdateInProgress
            {
                get
                {
                    if ( LockedAt.DeltaToNow > TickSpan.Minutes( 1 ) )
                    {
                        UpdateInProgressField = false;
                    }

                    return UpdateInProgressField;
                }
                set
                {
                    UpdateInProgressField = value;
                    if ( UpdateInProgressField )
                    {
                        LockedAt = TickCounter.Now;
                    }
                }
            }

            public ushort PendingSessionUpdate = 0;
            public List<ILease> PendingUpdate = null;
        }

        internal void MyDestination_SignLeasesRequest( ClientDestination dest, IEnumerable<ILease> leases )
        {
            Logging.LogDebug( $"{this} MyDestination_SignLeasesRequest: Received sign leases {leases?.Count()}" );

            lock ( PendingLeaseUpdate )
            {
                var sessid = FindSession( dest );
                if ( sessid == null )
                {
                    PendingLeaseUpdate.PendingUpdate = new List<ILease>( leases );
                    PendingLeaseUpdate.PendingSessionUpdate = 0;
                    return;
                }

                if ( sessid.MyDestination.Terminated )
                {
                    Terminate();
                    return;
                }

                if ( PendingLeaseUpdate.UpdateInProgress )
                {
                    PendingLeaseUpdate.PendingUpdate = new List<ILease>( leases );
                    PendingLeaseUpdate.PendingSessionUpdate = sessid.SessionId;
                    return;
                }

                Send( new RequestVariableLeaseSetMessage(
                        sessid.SessionId,
                        sessid.MyDestination.EstablishedLeases ) );

                PendingLeaseUpdate.UpdateInProgress = true;
            }
        }

        internal void SendPendingLeaseUpdates( bool nooutstanding = false )
        {
            lock ( PendingLeaseUpdate )
            {
                if ( nooutstanding )
                {
                    PendingLeaseUpdate.UpdateInProgress = false;
                    PendingLeaseUpdate.PendingUpdate = null;
                }

                if ( PendingLeaseUpdate.UpdateInProgress 
                        || PendingLeaseUpdate.PendingUpdate is null )
                {
                    return;
                }

                var ls = PendingLeaseUpdate.PendingUpdate;
                var sessionid = PendingLeaseUpdate.PendingSessionUpdate;
                if ( sessionid == 0 ) sessionid = SessionIds.First().Key;

                Logging.LogDebug( $"{this} SendPendingLeaseUpdates: Sending leases {ls?.Count()}" );

                Send( new RequestVariableLeaseSetMessage(
                        sessionid,
                        ls ) );

                PendingLeaseUpdate.UpdateInProgress = true;
            }
        }

        public I2CPMessage GetMessage( BufRefLen data )
        {
            var pmt = (ProtocolMessageType)data[4];
            data.Seek( 5 );

            Logging.LogDebug( $"{this} GetMessage: Received message {pmt}, {data.Length} bytes." );

            switch ( pmt )
            {
                case ProtocolMessageType.CreateSession:
                    return new CreateSessionMessage( data );

                case ProtocolMessageType.ReconfigSession:
                    return new ReconfigureSessionMessage( data );

                case ProtocolMessageType.DestroySession:
                    return new DestroySessionMessage( data );

                case ProtocolMessageType.CreateLS:
                    return new CreateLeaseSetMessage( data, this );

                case ProtocolMessageType.CreateLeaseSet2Message:
                    return new CreateLeaseSet2Message( data, this );

                case ProtocolMessageType.SendMessage:
                    break;

                case ProtocolMessageType.RecvMessageBegin:
                    break;

                case ProtocolMessageType.RecvMessageEnd:
                    return new ReceiveMessageEndMessage( data );

                case ProtocolMessageType.GetBWLimits:
                    break;

                case ProtocolMessageType.SessionStatus:
                    break;

                case ProtocolMessageType.RequestLS:
                    break;

                case ProtocolMessageType.MessageStatus:
                    break;

                case ProtocolMessageType.BWLimits:
                    break;

                case ProtocolMessageType.ReportAbuse:
                    break;

                case ProtocolMessageType.Disconnect:
                    break;

                case ProtocolMessageType.MessagePayload:
                    break;

                case ProtocolMessageType.GetDate:
                    return new GetDateMessage( data );

                case ProtocolMessageType.SetDate:
                    break;

                case ProtocolMessageType.DestLookup:
                    return new DestLookupMessage( data );

                case ProtocolMessageType.DestReply:
                    break;

                case ProtocolMessageType.SendMessageExpires:
                    return new SendMessageExpiresMessage( data );

                case ProtocolMessageType.RequestVarLS:
                    break;

                case ProtocolMessageType.HostLookup:
                    return new HostLookupMessage( data );

                case ProtocolMessageType.HostLookupReply:
                    break;

                default:
                    throw new ArgumentException( $"I2CPMessage:GetMessage I2CP message of type {data[4]} is unknown" );
            }

            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return $"{GetType().Name} {DebugId}";
        }
    }
}
