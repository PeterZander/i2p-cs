using NUnit.Framework;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using Org.BouncyCastle.Math;
using System.Net;
using I2PCore.TunnelLayer;
using System;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using I2PCore.SessionLayer;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace I2PTests
{
    [TestFixture]
    public class GarlicTest
    {
        private readonly I2PPrivateKey Private;
        private readonly I2PPublicKey Public;
        private readonly I2PRouterIdentity Me;

        private readonly I2PPrivateKey DestinationPrivate;
        private readonly I2PPublicKey DestinationPublic;
        private readonly I2PRouterIdentity Destination;

        private readonly I2PSigningPrivateKey PrivateSigning;
        private readonly I2PSigningPublicKey PublicSigning;

        private readonly CbcBlockCipher CBCAESCipher = new CbcBlockCipher( new AesEngine() );

        public GarlicTest()
        {
            Logging.LogToConsole = true;
            Logging.LogLevel = Logging.LogLevels.DebugData;

            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );

            Me = new I2PRouterIdentity( Public, new I2PSigningPublicKey( new BigInteger( "12" ), I2PKeyType.DefaultSigningKeyCert ) );

            DestinationPrivate = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            DestinationPublic = new I2PPublicKey( DestinationPrivate );
            Destination = new I2PRouterIdentity( DestinationPublic, new I2PSigningPublicKey( new BigInteger( "277626" ), I2PKeyType.DefaultSigningKeyCert ) );

            PrivateSigning = new I2PSigningPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigning = new I2PSigningPublicKey( PrivateSigning );
        }

        [Test]
        public void TestAESBlock()
        {
            for( int runs = 0; runs < 10; ++runs )
            {
                var buf = new BufLen( new byte[30000] );
                var writer = new BufRefLen( buf );

                var data = BufUtils.RandomBytes( 1 + BufUtils.RandomInt( 45 ) );
                var datar = new BufRefLen( data );
                var tags = new List<I2PSessionTag>();
                for ( int i = 0; i < BufUtils.RandomInt( 5 ); ++i )
                {
                    tags.Add( new I2PSessionTag() );
                }

                var newsession = BufUtils.RandomDouble( 1.0 ) < 0.3 ? new I2PSessionKey() : null;
                var b1 = new GarlicAESBlock( writer, tags, newsession, datar );

                var bldata = new BufLen( buf, 0, writer - buf ).Clone();

                var b2 = new GarlicAESBlock( new BufRefLen( bldata ) );

                var b1ar = new BufLen( b1.ToByteArray() );
                var b2ar = new BufLen( b2.ToByteArray() );
                Assert.IsTrue( b1ar == b2ar );

                var bufs = new BufRefStream();
                b1.Write( bufs );

                var b3 = new GarlicAESBlock( new BufRefLen( bufs.ToByteArray() ) );

                var b3ar = new BufLen( b3.ToByteArray() );
                Assert.IsTrue( b1ar == b3ar );
            }
        }

        [Test]
        public void TestGarlicCreateSmall()
        {
            var ls = new I2PDate( DateTime.Now + TimeSpan.FromMinutes( 5 ) );

            var origmessage = new DeliveryStatusMessage( I2NPMessage.GenerateMessageId() );

            var garlic = new Garlic(
                new GarlicClove(
                    new GarlicCloveDeliveryLocal( origmessage ) ) );

            var egmsg = Garlic.EGEncryptGarlic(
                    garlic,
                    Public,
                    new I2PSessionKey(),
                    null );

            var origegdata = I2NPMessage.Clone( egmsg );
            var origegdata2 = I2NPMessage.Clone( egmsg );

            // Decrypt

            var (aesblock, sessionkey1) = Garlic.EGDecryptGarlic( origegdata, Private );

            var newgarlic = new Garlic( (BufRefLen)aesblock.Payload );

            var g1 = new BufLen( garlic.ToByteArray() );
            var g2 = new BufLen( newgarlic.ToByteArray() );

            Assert.IsTrue( g1 == g2 );
            Assert.IsTrue( g2 == new BufLen( garlic.ToByteArray() ) );

            // Retrieve

            var (aesblock2, sessionkey2) = Garlic.RetrieveAESBlock( origegdata2, Private, null );

            newgarlic = new Garlic( (BufRefLen)aesblock2.Payload );

            g1 = new BufLen( garlic.ToByteArray() );
            g2 = new BufLen( newgarlic.ToByteArray() );

            Assert.IsTrue( g1 == g2 );
            Assert.IsTrue( sessionkey1 == sessionkey2 );
            Assert.IsTrue( g2 == new BufLen( garlic.ToByteArray() ) );
        }

        [Test]
        public void TestGarlicCreate()
        {
            var ls = new I2PDate( DateTime.Now + TimeSpan.FromMinutes( 5 ) );

            var origmessage = new DeliveryStatusMessage( I2NPMessage.GenerateMessageId() );
            var bigmessage = new DataMessage( new BufLen( BufUtils.RandomBytes( 14 * 1024 ) ) );

            var garlic = new Garlic(
                new GarlicClove(
                    new GarlicCloveDeliveryLocal( origmessage ),
                    ls ),
                new GarlicClove(
                    new GarlicCloveDeliveryLocal( bigmessage ),
                    ls )
            );

            var egmsg = Garlic.EGEncryptGarlic(
                    garlic,
                    Public,
                    new I2PSessionKey(),
                    null );

            var origegdata = I2NPMessage.Clone( egmsg );
            var origegdata2 = I2NPMessage.Clone( egmsg );

            // Decrypt

            var (aesblock,sessionkey1) = Garlic.EGDecryptGarlic( origegdata, Private );

            var newgarlic = new Garlic( (BufRefLen)aesblock.Payload );

            var g1 = new BufLen( garlic.ToByteArray() );
            var g2 = new BufLen( newgarlic.ToByteArray() );

            Assert.IsTrue( g1 == g2 );

            // Retrieve

            var (aesblock2,sessionkey2) = Garlic.RetrieveAESBlock( origegdata2, Private, null );

            newgarlic = new Garlic( (BufRefLen)aesblock2.Payload );

            g1 = new BufLen( garlic.ToByteArray() );
            g2 = new BufLen( newgarlic.ToByteArray() );

            Assert.IsTrue( g1 == g2 );
            Assert.IsTrue( sessionkey1 == sessionkey2 );
        }

        [Test]
        public void TestGarlicCreateMessage()
        {
            var dsm1 = new DeliveryStatusMessage( 0x425c )
            {
                MessageId = 2354
            };

            var dsm2 = new DeliveryStatusMessage( 0x425c )
            {
                MessageId = 2354
            };

            dsm2.Timestamp = dsm1.Timestamp;
            dsm2.Expiration = dsm1.Expiration;

            var dsm1h = dsm1.CreateHeader16;
            var dsm2h = dsm2.CreateHeader16;

            var dsm1hap = dsm1h.HeaderAndPayload;
            var dsm2hap = dsm2h.HeaderAndPayload;

            var st11 = FreenetBase64.Encode( dsm1hap );
            var st12 = FreenetBase64.Encode( dsm2hap );

            Assert.IsTrue( dsm1hap == dsm2hap );

            var gcd1 = new GarlicCloveDeliveryDestination(
                    dsm1,
                    Destination.IdentHash );

            var gcd2 = new GarlicCloveDeliveryDestination(
                    dsm2,
                    Destination.IdentHash );

            var gcd1ar = gcd1.ToByteArray();
            var gcd2ar = gcd2.ToByteArray();

            var st1 = FreenetBase64.Encode( new BufLen( gcd1ar ) );
            var st2 = FreenetBase64.Encode( new BufLen( gcd2ar ) );

            Assert.IsTrue( BufUtils.Equal( gcd1ar, gcd2ar ) );

            var msg1 = new GarlicClove( gcd1 );
            var msg2 = new GarlicClove( gcd2 );

            var g1 = new Garlic( msg1, msg2 );
            var g2 = new Garlic( new BufRefLen( g1.ToByteArray() ).Clone() );

            Assert.IsTrue( BufUtils.Equal( g1.ToByteArray(), g2.ToByteArray() ) );
        }

        [Test]
        public void TestEncodeDecodeEG()
        {
            var m1 = new DeliveryStatusMessage( 0x4321 );
            var m2 = new DeliveryStatusMessage( 0xa3c2 );

            var garlic = new Garlic(
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        m1,
                        Destination.IdentHash ) ),
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        m2,
                        Destination.IdentHash ) )
            );

            // No tags

            var cg = Garlic.EGEncryptGarlic( garlic, Public, new I2PSessionKey(), null );
            var egdata = I2NPMessage.Clone( cg );

            var (aesblock,sk) = Garlic.EGDecryptGarlic( egdata, Private );
            var g2 = new Garlic( (BufRefLen)aesblock.Payload );

            Assert.IsTrue( BufUtils.Equal( garlic.ToByteArray(), g2.ToByteArray() ) );

            // With tags
            var tags = new List<I2PSessionTag>();
            for ( int i = 0; i < 8; ++i )
            {
                tags.Add( new I2PSessionTag() );
            }

            cg = Garlic.EGEncryptGarlic( garlic, Public, new I2PSessionKey(), tags );
            egdata = I2NPMessage.Clone( cg );

            (aesblock, sk) = Garlic.EGDecryptGarlic( egdata, Private );
            g2 = new Garlic( (BufRefLen)aesblock.Payload );

            Assert.IsTrue( BufUtils.Equal( garlic.ToByteArray(), g2.ToByteArray() ) );
        }

        [Test]
        public void TestEncodeDecodeAES()
        {
            var m1 = new DeliveryStatusMessage( 0x4321 );
            var m2 = new DeliveryStatusMessage( 0xa3c2 );

            var garlic = new Garlic(
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        m1,
                        Destination.IdentHash ) ),
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        m2,
                        Destination.IdentHash ) )
            );

            // No tags

            var sessionkey = new I2PSessionKey();
            var sessiontag = new I2PSessionTag();

            var cg = Garlic.AESEncryptGarlic( garlic, sessionkey, sessiontag, null );
            var egdata = I2NPMessage.Clone( cg );

            var (aesblock, sk) = Garlic.RetrieveAESBlock( egdata, Private, (t) => sessionkey );
            var g2 = new Garlic( (BufRefLen)aesblock.Payload );

            Assert.IsTrue( BufUtils.Equal( garlic.ToByteArray(), g2.ToByteArray() ) );

            // With tags
            var tags = new List<I2PSessionTag>();
            for ( int i = 0; i < 30; ++i )
            {
                tags.Add( new I2PSessionTag() );
            }

            cg = Garlic.AESEncryptGarlic( garlic, sessionkey, sessiontag, null );
            egdata = I2NPMessage.Clone( cg );

            (aesblock, sk) = Garlic.RetrieveAESBlock( egdata, Private, ( t ) => sessionkey );
            g2 = new Garlic( (BufRefLen)aesblock.Payload );

            Assert.IsTrue( BufUtils.Equal( garlic.ToByteArray(), g2.ToByteArray() ) );
        }

        class TunnelOwner : ITunnelOwner
        {
            public void TunnelBuildTimeout( Tunnel tunnel )
            {
            }

            public void TunnelEstablished( Tunnel tunnel )
            {
            }

            public void TunnelExpired( Tunnel tunnel )
            {
            }

            public void TunnelFailed( Tunnel tunnel )
            {
            }
        }

        public class CaptureOutTunnel: OutboundTunnel
        {
            public ConcurrentQueue<TunnelMessage> SendQueueAccess { get => SendQueue; }

            public CaptureOutTunnel( ITunnelOwner owner, TunnelConfig config ): base( owner, config, 3 )
            {

            }
        }

        class TestSessionKeyOrigin : SessionKeyOrigin
        {
            public void DeliveryStatusReceived( DeliveryStatusMessage msg )
            {
                InboundTunnel_DeliveryStatusReceived( msg );
            }

            public TestSessionKeyOrigin( 
                    ClientDestination owner,
                    I2PDestination mydest, 
                    I2PDestination remotedest )
                        : base( owner, mydest, remotedest )
            {

            }
        }

        [Test]
        public void TestEncodeDecodeLoop()
        {
            var m1 = new DeliveryStatusMessage( 0x4321 );
            var m2 = new DeliveryStatusMessage( 0xa3c2 );

            var cloves = new List<GarlicClove>()
            {
                new GarlicClove(
                    new GarlicCloveDeliveryDestination(
                        m1,
                        Destination.IdentHash ) ),
                new GarlicClove(
                    new GarlicCloveDeliveryLocal(
                        new DataMessage( new BufLen( BufUtils.RandomBytes( 3000 ) ) ) ) ),
                new GarlicClove(
                    new GarlicCloveDeliveryLocal(
                        new TunnelGatewayMessage( m1, new I2PTunnelId() ) ) ),
                new GarlicClove(
                    new GarlicCloveDeliveryLocal( m2 ) )
            };

            var originfo = new I2PDestinationInfo( 
                    I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            var origdest = originfo.Destination;

            var destinfo = new I2PDestinationInfo(
                    I2PSigningKey.SigningKeyTypes.ECDSA_SHA256_P256 );
            var destdest = destinfo.Destination;

            var publishedleases = new I2PLeaseSet(
                origdest,
                new I2PLease[] {
                    new I2PLease( new I2PIdentHash( true ), new I2PTunnelId() )
                },
                new I2PLeaseInfo( originfo ) );

            var origko = new TestSessionKeyOrigin( null, origdest, destdest );

            var recv = new DecryptReceivedSessions( "recv", originfo.PrivateKey );

            CaptureOutTunnel outtunnel = new CaptureOutTunnel( 
                    new TunnelOwner(), 
                    new TunnelConfig(
                        TunnelConfig.TunnelDirection.Outbound,
                        TunnelConfig.TunnelPool.Client,
                        new TunnelInfo( new List<HopInfo>
                        {
                            new HopInfo(
                                Destination,
                                new I2PTunnelId() )
                        }
                    ) ) );

            var replytunnel = new ZeroHopTunnel(
                    new TunnelOwner(),
                    new TunnelConfig(
                        TunnelConfig.TunnelDirection.Inbound,
                        TunnelConfig.TunnelPool.Exploratory,
                        new TunnelInfo( new List<HopInfo>()
                        {
                            new HopInfo( Destination, new I2PTunnelId() )
                        } ) ) );

            for ( int i = 0; i < 100; ++i )
            {
                origko.Send( 
                        outtunnel, 
                        publishedleases,
                        publishedleases,
                        () => replytunnel,
                        false,
                        cloves.ToArray() );

                Assert.IsTrue( outtunnel.SendQueueAccess.TryDequeue( out var tmsg ) );
                var recgarlic = recv.DecryptMessage( (GarlicMessage)tmsg.Message );

                Assert.IsTrue( 
                        cloves.All( origclove => 
                            recgarlic.Cloves.Any( c => 
                                c.Message.Payload == origclove.Delivery.Message.Payload ) ) );

                var dsmsgs = recgarlic.Cloves
                            .Where( c => c.Message.MessageType == I2NPMessage.MessageTypes.DeliveryStatus )
                            .Select( c => c.Message );

                foreach ( DeliveryStatusMessage dsm in dsmsgs )
                {
                    origko.DeliveryStatusReceived( dsm );
                }

                cloves.Clear();
                for ( int j = 0; j < 2 + BufUtils.RandomInt( 4 ); ++j )
                {
                    GarlicCloveDelivery gcd = null;

                    var msg = CreateDatabaseStoreMessage();

                    switch ( BufUtils.RandomInt( 4 ) )
                    {
                        case 0: gcd = new GarlicCloveDeliveryLocal( msg ); break;
                        case 1: gcd = new GarlicCloveDeliveryRouter( msg, new I2PIdentHash( true ) ); break;
                        case 2: gcd = new GarlicCloveDeliveryTunnel( msg, new I2PIdentHash( true ), new I2PTunnelId() ); break;
                        case 3: gcd = new GarlicCloveDeliveryRouter( msg, new I2PIdentHash( true ) ); break;
                    }

                    cloves.Add( new GarlicClove( gcd ) );
                }

                cloves.Add( new GarlicClove(
                    new GarlicCloveDeliveryLocal(
                        new DataMessage( new BufLen( BufUtils.RandomBytes( 3000 ) ) ) ) ) );
            }
        }

        DatabaseStoreMessage CreateDatabaseStoreMessage()
        {
            var mapping = new I2PMapping();
            mapping["One"] = "1";
            mapping["2"] = "Two";

            var ri = new I2PRouterInfo(
                new I2PRouterIdentity( Public, PublicSigning ),
                I2PDate.Now,
                new I2PRouterAddress[] { new I2PRouterAddress( new IPAddress( 424242L ), 773, 42, "SSU" ) },
                mapping,
                PrivateSigning );

            var dbsm = new DatabaseStoreMessage( ri );

            return dbsm;
        }

/*
 * The maximum size of an initial fragment is 956 bytes (assuming TUNNEL delivery mode); the 
 * maximum size of a follow-on fragment is 996 bytes. Therefore the maximum size is 
 * approximately 956 + (62 * 996) = 62708 bytes, or 61.2 KB.

 * In addition, the transports may have additional restrictions. 
 * NTCP currently limits to 16KB - 6 = 16378 bytes but this will be increased in a future release. 
 * The SSU limit is approximately 32 KB.

 * Note that these are not the limits for datagrams that the client sees, as the router may 
 * bundle a reply leaseset and/or session tags together with the client message in a garlic message. 
 * The leaseset and tags together may add about 5.5KB. Therefore the current datagram limit is about 10KB. 
 * This limit will be increased in a future release.
 * https://geti2p.net/en/docs/protocol/i2np
 */
        [Test]
        public void TestBiggerEncodeDecode()
        {
            var recv = new DecryptReceivedSessions( this, Private );

            var origdsmessage = new DeliveryStatusMessage( 0x425c );
            var datamessage = new DataMessage( new BufLen( BufUtils.RandomBytes( 16000 ) ) );
            var origmessage1 = CreateDatabaseStoreMessage();
            var origmessage2 = CreateDatabaseStoreMessage();
            var origmessage3 = CreateDatabaseStoreMessage();
            var origmessage4 = CreateDatabaseStoreMessage();

            /*
            var msg1eg = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origmessage1,
                    Destination.IdentHash,
                    1234 ),
                new GarlicCloveDeliveryLocal(
                    origmessage2 ),
                new GarlicCloveDeliveryRouter(
                    origmessage3,
                    Destination.IdentHash )
                );

            var msg2aes = session.CreateMessage(
                Me,
                new GarlicCloveDeliveryTunnel(
                    origdsmessage,
                    Destination.IdentHash,
                    1234 ),
                new GarlicCloveDeliveryRouter(
                    origmessage1,
                    Destination.IdentHash ),
                new GarlicCloveDeliveryLocal(
                    datamessage ),
                new GarlicCloveDeliveryTunnel(
                    origmessage4,
                    Destination.IdentHash,
                    1234 ) );

            var dmsg1eg = recv.DecryptMessage( msg1eg.Garlic );
            var dmsg2aes = recv.DecryptMessage( msg2aes.Garlic );

            Assert.IsTrue( dmsg1eg.Cloves.Count() == 3 );
            Assert.IsTrue( dmsg2aes.Cloves.Count() == 4 );

            Assert.IsTrue( dmsg2aes.Cloves[0].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Tunnel );
            Assert.IsTrue( dmsg2aes.Cloves[1].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Router );
            Assert.IsTrue( dmsg2aes.Cloves[2].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Local );
            Assert.IsTrue( dmsg2aes.Cloves[3].Delivery.Delivery == GarlicCloveDelivery.DeliveryMethod.Tunnel );

            Assert.IsTrue( dmsg1eg.Cloves.All( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ) );
            Assert.IsTrue( !dmsg2aes.Cloves.All( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ) );

            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DeliveryStatus ).
                All( m => ( (DeliveryStatusMessage)m.Message ).MessageId == 0x425c ) );

            Assert.IsTrue( dmsg1eg.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ).
                All( m => ( (DatabaseStoreMessage)m.Message ).RouterInfo.VerifySignature() ) );
            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.DatabaseStore ).
                All( m => ( (DatabaseStoreMessage)m.Message ).RouterInfo.VerifySignature() ) );
            Assert.IsTrue( dmsg2aes.Cloves.Where( m => m.Message.MessageType == I2NPMessage.MessageTypes.Data ).
                All( m => ( (DataMessage)m.Message ).Payload == datamessage.Payload ) );
             */
        }
        
        [Test]
        public void TestTooBigEncodeDecode()
        {
            var recv = new DecryptReceivedSessions( this, Private );

            /*
            var datamessage = new DataMessage( new BufLen( BufUtils.Random( 65000 ) ) );

            try
            {
                var msg1eg = session.CreateMessage(
                    Me,
                    new GarlicCloveDeliveryTunnel(
                        datamessage,
                        Destination.IdentHash,
                        1234 )
                    );

                Assert.Fail();
            }
            catch ( Exception )
            {
                Assert.IsTrue( true );
            }
             */
        }
    }
}
