using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using I2PCore.Utils;
using I2PCore.Data;
using System.IO;
using System.Net;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Transport.NTCP
{
    public abstract class NTCPClient: ITransport
    {
#if DEBUG
        public const int SendQueueLengthWarningLimit = 50;
#endif
        public const int SendQueueLengthUpperLimit = 200;

        public readonly int InactivityTimeoutSeconds = 
            (int)I2PCore.Tunnel.Tunnel.TunnelLifetime.ToSeconds;
        
        protected Socket MySocket;

        protected Thread Worker;
        public bool Terminated = false;
        bool ITransport.Terminated { get { return Terminated; } }

        public abstract IPAddress RemoteAddress { get; }

        protected string RemoteDescription = "";

        public event Action<ITransport> ConnectionShutDown;
        public event Action<ITransport, Exception> ConnectionException;

        public long BytesSent { get; protected set; }
        public long BytesReceived { get; protected set; }

        /// <summary>
        /// Diffie-Hellman negotiations completed.
        /// </summary>
        public event Action<ITransport,I2PIdentHash> ConnectionEstablished;

        public event Action<ITransport, II2NPHeader> DataBlockReceived;

        public string DebugId { get { return "+" + TransportInstance.ToString() + "+"; } }
        public string Protocol { get => "NTCP"; }
        public bool Outgoing { get => mOutgoing; }
        private readonly bool mOutgoing;

        protected NTCPClient( bool outgoing )
        {
            mOutgoing = outgoing;
            TransportInstance = Interlocked.Increment( ref TransportInstanceCounter );
        }

        protected abstract Socket CreateSocket();

        bool WorkerRunning = false;

        public virtual void Connect()
        {
            if ( WorkerRunning ) return;

            Worker = new Thread( Run )
            {
                Name = "NTCPClient",
                IsBackground = true
            };
            Worker.Start();

            WorkerRunning = true;
        }

        public void Terminate()
        {
            Terminated = true;
            if ( Worker != null )
            {
                Worker.Abort();
                if ( !Worker.Join( 100 ) ) Worker.Interrupt();
            }
        }

        LinkedList<I2NPMessage> SendQueue = new LinkedList<I2NPMessage>();
        LinkedList<BufLen> SendQueueRaw = new LinkedList<BufLen>();
#if DEBUG
        TickCounter MinTimeBetweenSendQueueLogs = new TickCounter();
        int SessionMaxSendQueueLength = 0;
#endif

        public void Send( I2NPMessage msg )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            var sendqlen = SendQueue.Count;
#if DEBUG
            SessionMaxSendQueueLength = Math.Max( SessionMaxSendQueueLength, sendqlen );

            if ( ( SendQueue.Count > SendQueueLengthWarningLimit ) && ( MinTimeBetweenSendQueueLogs.DeltaToNowMilliseconds > 4000 ) )
            {
                Logging.LogWarning(
                    string.Format( "NTCPClient {0}: SendQueue is {1} messages long! Max queue: {2} ({3:###0}s)",
                    DebugId, sendqlen, SessionMaxSendQueueLength, MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds ) );
                MinTimeBetweenSendQueueLogs.SetNow();
            }
#endif

            lock ( SendQueue )
            {
                if ( sendqlen < SendQueueLengthUpperLimit ) SendQueue.AddLast( msg );
#if DEBUG
                else
                {
                    Logging.LogWarning(
                        string.Format( "NTCPClient {0}: SendQueue is {1} messages long! Dropping new message. Max queue: {2} ({3:###0}s)",
                        DebugId, sendqlen, SessionMaxSendQueueLength, MinTimeBetweenSendQueueLogs.DeltaToNow.ToSeconds ) );
                }
#endif
            }

            try
            {
                TryInitiateSend();
            }
            catch ( SocketException ex )
            {
                if ( NTCPContext.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.FailedToConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                throw new EndOfStreamEncounteredException( ex.ToString() );
            }
        }

        AutoResetEvent SendSocketFree = new AutoResetEvent( true );

        protected void TryInitiateSend()
        {
            if ( !SendSocketFree.WaitOne( 0 ) ) return;

            BufLen data;

            if ( SendQueueRaw.Count > 0 )
            {
                lock ( SendQueueRaw )
                {
                    data = SendQueueRaw.First.Value;
                    SendQueueRaw.RemoveFirst();
                }
            }
            else if ( DHSucceeded )
            {
                I2NPMessage msg;
                lock ( SendQueue )
                {
                    if ( SendQueue.Count == 0 )
                    {
                        SendSocketFree.Set();
                        return;
                    }
                    msg = SendQueue.First.Value;
                    SendQueue.RemoveFirst();
                }

                Watchdog.Inst.Ping( Thread.CurrentThread );

                data = GenerateData( msg );
            }
            else
            {
                SendSocketFree.Set();
                return;
            }

            MySocket.BeginSend( data.BaseArray, data.BaseArrayOffset, data.Length, SocketFlags.None, new AsyncCallback( SendCompleted ), null );
        }

        void SendCompleted( IAsyncResult ar )
        {
            try
            {
                var cd = MySocket.EndSend( ar );
#if LOG_MUCH_TRANSPORT
                //Logging.LogTransport( string.Format( "NTCP {1} Async complete: {0} bytes [0x{0:X}]", cd, DebugId ) );
#endif
            }
            catch ( Exception ex )
            {
                Logging.Log( "NTCP SendCompleted", ex );
                Terminated = true;
            }
            finally
            {
                SendSocketFree.Set();
            }

            try
            {
                TryInitiateSend();
            }
            catch ( Exception ex )
            {
                Logging.Log( "NTCP SendCompleted TryInitiateSend", ex );
                Terminated = true;
            }
        }

        public BufLen GenerateData( I2NPMessage msg )
        {
            if ( NTCPContext == null ) throw new Exception( "NTCP Session not negotiated!" );
            if ( NTCPContext.Encryptor == null ) throw new Exception( "NTCP encryptor not available" );

            var data = msg != null ? msg.Header16.HeaderAndPayload: null;

            var datalength = msg == null ? 4 : data.Length;
            var buflen = 2 + datalength + 4;
            var padlength = BufUtils.Get16BytePadding( buflen );
            buflen += padlength;

            var buf = new BufLen( new byte[buflen] );
            var writer = new BufRefLen( buf );

            // Length
            if ( msg == null )
            {
                // Send timestamp
                writer.Write16( 0 );
                writer.WriteFlip32( (uint)( DateTime.UtcNow - I2PDate.RefDate ).TotalSeconds );
            }
            else
            {
                if ( data.Length > 16378 ) throw new ArgumentException( "NTCP data can be max 16378 bytes long!" );
                writer.WriteFlip16( (ushort)data.Length );
                writer.Write( data );
            }

            // Pad
            writer.Write( BufUtils.Random( writer.Length - 4 ) );

            // Checksum
            var checkbuf = new BufLen( buf, 0, writer - buf );
            var checksum = LZUtils.Adler32( 1, checkbuf );
            writer.WriteFlip32( checksum );

            // Encrypt
            NTCPContext.Encryptor.ProcessBytes( buf );

            return buf;
        }

        protected void SendRaw( byte[] data )
        {
            SendRaw( new BufLen( data ) );
        }

        protected void SendRaw( BufLen data )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            lock ( SendQueueRaw )
            {
#if LOG_MUCH_TRANSPORT
                Logging.LogTransport( string.Format( "NTCP {1} Raw sent: {0} bytes [0x{0:X}]", data.Length, DebugId ) );
#endif
                SendQueueRaw.AddLast( data );
            }

            TryInitiateSend();
        }

        internal BufLen BlockReceiveAtLeast( int bytes, int maxlen )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            var buf = new byte[Math.Max( maxlen, bytes )];
            var pos = 0;
            while ( pos < bytes )
            {
                var len = MySocket.Receive( buf, pos, buf.Length - pos, SocketFlags.None );
                if ( len == 0 )
                {
                    throw new EndOfStreamEncounteredException();
                }
                else
                {
#if LOG_MUCH_TRANSPORT
                    Logging.LogTransport( string.Format( "NTCP {1} Recvatl ({2}): {0} bytes [0x{0:X}]", len, DebugId, bytes ) );
#endif
                    pos += len;
                }
            }

            return new BufLen( buf, 0, pos );
        }

        internal BufLen BlockReceive( int bytes )
        {
            if ( Terminated ) throw new EndOfStreamEncounteredException();

            var buf = new byte[bytes];
            var pos = 0;
            while ( pos < bytes )
            {
                var len = MySocket.Receive( buf, pos, buf.Length - pos, SocketFlags.None );
                if ( len == 0 )
                {
                    throw new EndOfStreamEncounteredException();
                }
                else
                {
#if LOG_MUCH_TRANSPORT
                    Logging.LogTransport( string.Format( "NTCP {1} Blockr ({2}): {0} bytes [0x{0:X}]", len, DebugId, bytes ) );
#endif
                    pos += len;
                }
            }

            return new BufLen( buf );
        }

        public NTCPRunningContext NTCPContext = new NTCPRunningContext();

        public I2PKeysAndCert RemoteRouterIdentity
        {
            get
            {
                return NTCPContext.RemoteRouterIdentity;
            }
        }

        bool DHSucceeded;

        internal static int TransportInstanceCounter = 0;
        protected int TransportInstance;

        void Run()
        {
            try
            {
                try
                {
                    NTCPContext.TransportInstance = TransportInstance;

                    Watchdog.Inst.StartMonitor( Thread.CurrentThread, 20000 );

                    try
                    {
                        MySocket = CreateSocket();

                        RemoteDescription = MySocket.RemoteEndPoint.ToString();

#if LOG_MUCH_TRANSPORT
                        Logging.LogTransport( "My local endpoint IP#   : " + ( (IPEndPoint)MySocket.LocalEndPoint ).Address.ToString() );
                        Logging.LogTransport( "My local endpoint Port  : " + ( (IPEndPoint)MySocket.LocalEndPoint ).Port.ToString() );
#endif

                        DHNegotiate();
                    }
                    catch ( SocketException ex )
                    {
                        if ( NTCPContext.RemoteRouterIdentity != null )
                            NetDb.Inst.Statistics.FailedToConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                        throw new FailedToConnectException( ex.ToString() );
                    }
                    catch ( FormatException )
                    {
                        if ( NTCPContext.RemoteRouterIdentity != null )
                            NetDb.Inst.Statistics.DestinationInformationFaulty( NTCPContext.RemoteRouterIdentity.IdentHash );
                        throw;
                    }
                    catch ( NotSupportedException )
                    {
                        if ( NTCPContext.RemoteRouterIdentity != null )
                            NetDb.Inst.Statistics.DestinationInformationFaulty( NTCPContext.RemoteRouterIdentity.IdentHash );
                        throw;
                    }
                    catch ( ThreadInterruptedException )
                    {
                        if ( NTCPContext.RemoteRouterIdentity != null )
                            NetDb.Inst.Statistics.SlowHandshakeConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                        throw;
                    }
                    catch ( ThreadAbortException )
                    {
                        if ( NTCPContext.RemoteRouterIdentity != null )
                            NetDb.Inst.Statistics.SlowHandshakeConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                        throw;
                    }

                    DHSucceeded = true;

                    Watchdog.Inst.UpdateTimeout( Thread.CurrentThread, InactivityTimeoutSeconds * 1000 );

                    TryInitiateSend();

#if DEBUG
                    if ( ConnectionEstablished == null ) Logging.LogWarning( "NTCPClient: No observers for ConnectionEstablished!" );
#endif
                    ConnectionEstablished?.Invoke( this, RemoteRouterIdentity.IdentHash );

                    NetDb.Inst.Statistics.SuccessfulConnect( NTCPContext.RemoteRouterIdentity.IdentHash );

                    var reader = new NTCPReader( MySocket, NTCPContext );

                    while ( !Terminated )
                    {
                        var data = reader.Read();
                        //Logging.LogTransport( "Read: " + data.Length );

                        Watchdog.Inst.Ping( Thread.CurrentThread );

                        if ( reader.NTCPDataSize == 0 )
                        {
                            //if ( TimeSyncReceived != null ) TimeSyncReceived( this, data );
                        }
                        else
                        {
#if DEBUG
                            if ( DataBlockReceived == null ) Logging.LogWarning( "NTCPClient: No observers for DataBlockReceived !" );
#endif
                            if ( DataBlockReceived != null ) DataBlockReceived( this, I2NPMessage.ReadHeader16( new BufRefLen( data ) ) );
                        }
                    }
                }
                catch ( ThreadAbortException )
                {
                    Logging.LogTransport( string.Format( "NTCP {0} Aborted", DebugId ) );
                }
                catch ( ThreadInterruptedException )
                {
                    Logging.LogTransport( string.Format( "NTCP {0} Interrupted", DebugId ) );
                }
                catch ( FailedToConnectException )
                {
                    Logging.LogTransport( string.Format( "NTCP {0} Failed to connect", DebugId ) );
                }
                catch ( EndOfStreamEncounteredException )
                {
                    Logging.LogTransport( string.Format( "NTCP {0} Disconnected", DebugId ) );
                }
                catch ( IOException ex )
                {
                    Logging.LogTransport( string.Format( "NTCP {1} Exception: {0}", ex, DebugId ) );
                }
                catch ( SocketException ex )
                {
                    Logging.LogTransport( string.Format( "NTCP {1} Exception: {0}", ex, DebugId ) );
                }
                catch ( Exception ex )
                {
#if DEBUG
                    if ( ConnectionException == null ) Logging.LogWarning( "NTCPClient: No observers for ConnectionException!" );
#endif
                    if ( ConnectionException != null ) ConnectionException( this, ex );
                    Logging.LogTransport( string.Format( "NTCP {1} Exception: {0}", ex, DebugId ) );
                }
            }
        
            finally
            {
                Terminated = true;
                Watchdog.Inst.StopMonitor( Thread.CurrentThread );

                try
                {
                    if ( !DHSucceeded && NTCPContext.RemoteRouterIdentity != null )
                        NetDb.Inst.Statistics.FailedToConnect( NTCPContext.RemoteRouterIdentity.IdentHash );
                }
                catch ( Exception ex )
                {
                    Logging.Log( DebugId, ex );
                }

                Logging.LogTransport( string.Format( "NTCP {0} Shut down. {1}", DebugId, RemoteDescription ) );

                try
                {
#if DEBUG
                    if ( ConnectionShutDown == null ) Logging.LogWarning( "NTCPClient: No observers for ConnectionShutDown!" );
#endif
                    if ( ConnectionShutDown != null ) ConnectionShutDown( this );
                }
                catch ( Exception ex )
                {
                    Logging.Log( DebugId, ex );
                }

                if ( MySocket != null )
                {
                    try
                    {
                        MySocket.Shutdown( SocketShutdown.Both );
                        MySocket.Close();
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( DebugId, ex );
                    }
                    finally
                    {
                        MySocket = null;
                    }
                }
            }
        }

        protected abstract void DHNegotiate();
    }
}
