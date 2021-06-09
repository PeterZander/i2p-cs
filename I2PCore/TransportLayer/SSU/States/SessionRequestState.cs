#define LOG_SIG_FAIL_HOSTS

using System;
using System.Linq;
using I2PCore.Utils;
using System.Net;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using I2PCore.Data;
using I2PCore.TransportLayer.SSU.Data;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace I2PCore.TransportLayer.SSU
{
    public partial class SessionRequestState: SSUState
    {
        internal I2PPrivateKey PrivateKey;
        internal I2PPublicKey X;
        internal I2PPublicKey Y;

        internal SessionCreated SCMessage;

        protected readonly bool RemoteIsFirewalled;

        public SessionRequestState( SSUSession sess, bool remoteisfirewalled ): base( sess )
        {
            RemoteIsFirewalled = remoteisfirewalled;

            var keys = I2PPrivateKey.GetNewKeyPair();
            PrivateKey = keys.PrivateKey;
            X = keys.PublicKey;
        }

        protected override BufLen CurrentMACKey { get { return Session.RemoteIntroKey; } }
        protected override BufLen CurrentPayloadKey { get { return Session.RemoteIntroKey; } }

        PeriodicAction ResendSessionRequestAction = new PeriodicAction( HandshakeStateTimeout / 2, true );

        public override SSUState Run()
        {
            if ( Timeout( HandshakeStateTimeout * 4 ) )
            {
                Session.Host.EPStatisitcs.ConnectionTimeout( Session.RemoteEP );
                if ( Session.RemoteRouterIdentity != null )
                    NetDb.Inst.Statistics.SlowHandshakeConnect( Session.RemoteRouterIdentity.IdentHash );

                throw new FailedToConnectException( $"SSU {this}: Failed to connect. Timeout." );
            }

            ResendSessionRequestAction.Do( () =>
            {
                if ( ++Retries > HandshakeStateMaxRetries )
                    throw new FailedToConnectException( $"SSU {this}: Failed to connect. Too many retries." );

                SendSessionRequest();
            } );

            return this;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            var tstime = SSUHost.SSUDateTime( header.TimeStamp );

            if ( header.MessageType != SSUHeader.MessageTypes.SessionCreated )
            {
                Logging.LogTransport( $"SSU SessionRequestState: Received unexpected message {tstime} : {header.Flag}" );
                return this;
            }

            SCMessage = new SessionCreated( reader, Session.RemoteCert );

            Session.RelayTag = SCMessage.RelayTag;

            Y = new I2PPublicKey( (BufRefLen)SCMessage.Y, Session.RemoteCert );
            BufUtils.DHI2PToSessionAndMAC( out var sessionkey, out var mackey,
                Y.ToBigInteger().ModPow( PrivateKey.ToBigInteger(), I2PConstants.ElGamalP ) );

            Session.SharedKey = sessionkey;
            Session.MACKey = mackey;

            var cipher = new CbcBlockCipher( new AesEngine() );
            cipher.Init( false, Session.SharedKey.ToParametersWithIV( header.IV ) );
            cipher.ProcessBytes( SCMessage.SignatureEncrBuf );

            var baddr = new BufLen( Session.UnwrappedRemoteAddress.GetAddressBytes() );
            var sign = new I2PSignature( (BufRefLen)SCMessage.Signature, Session.RemoteCert );

            var sok = I2PSignature.DoVerify(
                Session.RemoteRouterIdentity.SigningPublicKey, sign,
                X.Key, Y.Key, 
                SCMessage.Address, SCMessage.Port,
                baddr, BufUtils.Flip16BL( (ushort)Session.RemoteEP.Port ), 
                SCMessage.RelayTag, SCMessage.SignOnTime );

            Logging.LogDebugData( $"SSU {this}: Signature check: {sok}. {Session.RemoteCert.SignatureType}, {SCMessage.SignatureEncrBuf.Length}, {SCMessage.Address.Length}" );

#if DEBUG
            Session.Host.SignatureChecks.Success( sok );
            if ( !sok )
            {
                Logging.LogDebug( $"SSU {this}: " + 
                    $"Signature checks success: {Session.Host.SignatureChecks} " );

#if LOG_SIG_FAIL_HOSTS

                DebugLogSignatureCheckFailures.Do( () => 
                {
                    var scfa = SignatureCheckFailures
                            .OrderByDescending( scf => scf.Value.Failures.Count )
                            .ToArray();

                    foreach( var scf in scfa )
                    {
                        if ( scf.Value.RouterVersion == null )
                        {
                            var fria = NetDb.Inst.FindRouterInfo( ( id, ri ) => 
                                    ri.Adresses.Any( a =>
                                    {
                                        var hostaddr = IPAddress.TryParse( a.Options.TryGet( "host" )?.ToString(), out var ha ) ? ha : null;
                                        return scf.Key?.Equals( hostaddr ) ?? false;
                                    } ) );

                            if ( fria.Any() )
                            {
                                scf.Value.RouterVersion = fria.First().Options.TryGet( "router.version" )?.ToString() ?? "Unknown";
                            }
                            else
                            {
                                scf.Value.RouterVersion = "Not found";
                            }
                        }
                        Logging.LogDebug( $"SessionRequestState: Signature check failures: Host: {scf.Key,50}, " + 
                            $"Failures: {scf.Value.Failures.Count,7}, {scf.Value.RouterVersion}" );
                    }
                } );
#endif                
            }
#endif

            if ( !sok )
            {
                var fi = SignatureCheckFailures.GetOrAdd( 
                        Session.UnwrappedRemoteAddress,
                        ( s ) => new SignatureCheckFailInfo() );
                fi.Failures[DateTime.UtcNow] = 0;

                var aliceip = SCMessage.Address.Length == 4 || SCMessage.Address.Length == 16
                        ? new IPAddress( SCMessage.Address.ToByteArray() )
                        : IPAddress.Loopback;
                var aliceport = BufUtils.Flip16( SCMessage.Port.ToArray(), 0 );
                Logging.LogDebug( $"SSU {this}: Signature check: {sok}. " +
                        $"Router info: {Session.RemoteRouterInfo?.PublishedDate}" );
                Logging.LogTransport( () => $"SSU {this}: Signature check: {sok}. " +
                        $"My ip: {aliceip} {SCMessage.Address}, Port: {aliceport} {SCMessage.Port}, " +
                        $"Bob addr: {Session.UnwrappedRemoteAddress} {baddr}, Bob port: {Session.RemoteEP.Port}" );

                SendSessionDestroyed();

                throw new SignatureCheckFailureException( $"SSU {this}: Received SessionCreated signature check failed." +
                    $"{Session.RemoteCert}" );
            }

            try
            {
                var ipaddr = new IPAddress( SCMessage.Address.ToByteArray() );
                ushort port = SCMessage.Port.PeekFlip16( 0 );
                Session.SignOnTimeB = SCMessage.SignOnTime.Peek32( 0 );
                var btime = SSUHost.SSUDateTime( BufUtils.Flip32( Session.SignOnTimeB ) );

                Logging.LogTransport( $"SSU SessionRequestState {Session.DebugId} : Received SessionCreated. {tstime.ToString()} : {btime}" );
                Session.Host.ReportedAddress( ipaddr );
            }
            catch( ArgumentException ex )
            {
                Logging.LogDebug( $"SSU SessionRequestState ArgumentException {ex.Message} Addr: {SCMessage.Address} Cert: {Session.RemoteCert}" );
                
                throw;
            }

            if ( !RemoteIsFirewalled )
            {
                var relaytag = SCMessage.RelayTag.PeekFlip32( 0 );
                if ( relaytag != 0 )
                {
                    Session.RemoteIntroducerInfo = new IntroducerInfo(
                            Session.RemoteEP.Address,
                            (ushort)Session.RemoteEP.Port,
                            Session.RemoteIntroKey, relaytag );

                    Session.Host.IntroductionRelayOffered( Session.RemoteIntroducerInfo );
                }
            }

            Logging.LogTransport( $"SSU {this}: SessionCreated received " +
            	$"from {Session.RemoteEP} created." );

            return SendConnectionEstablished();
        }

        private void SendSessionRequest()
        {
            Logging.LogTransport( $"SSU SessionRequestState {Session.DebugId}: " +
                $"sending SessionRequest message to {Session.RemoteEP}." );

            SendMessage(
                SSUHeader.MessageTypes.SessionRequest,
                Session.RemoteIntroKey,
                Session.RemoteIntroKey,
                ( start, writer ) =>
                {
                    writer.Write( X.Key.ToByteArray() );
                    var addr = Session.UnwrappedRemoteAddress.GetAddressBytes();
                    writer.Write8( (byte)addr.Length );
                    writer.Write( addr );

                    return true;
                } );
        }

#if LOG_SIG_FAIL_HOSTS
        static PeriodicAction DebugLogSignatureCheckFailures = new PeriodicAction( TickSpan.Minutes( 10 ) );
#endif

        internal class SignatureCheckFailInfo
        {
            public TimeWindowDictionary<DateTime,object> Failures { get; set; } = new TimeWindowDictionary<DateTime, object>( TickSpan.Hours( 5 ) );
            public string RouterVersion { get; set; }
        }

        internal static ConcurrentDictionary<IPAddress,SignatureCheckFailInfo> 
                SignatureCheckFailures = new ConcurrentDictionary<IPAddress, SignatureCheckFailInfo>( new IPAddressComparer() );

        class IPAddressComparer : IEqualityComparer<IPAddress>
        {
            public bool Equals(IPAddress x, IPAddress y)
            {
                if ( x is null || y is null ) return false;
                return x.Equals( y );
            }

            public int GetHashCode([DisallowNull] IPAddress obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
