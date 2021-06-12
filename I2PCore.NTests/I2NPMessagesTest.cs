using System.Collections.Generic;
using NUnit.Framework;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using Org.BouncyCastle.Math;
using System.Net;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Linq;

namespace I2PTests
{
    [TestFixture]
    public class I2NPMessagesTest
    {
        I2PPrivateKey Private;
        I2PPublicKey Public;
        I2PSigningPrivateKey PrivateSigning;
        I2PSigningPublicKey PublicSigning;
        I2PSigningPrivateKey PrivateSigningEd25519;
        I2PSigningPublicKey PublicSigningEd25519;
        I2PRouterIdentity Me;

        public I2NPMessagesTest()
        {
            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );
            PrivateSigning = new I2PSigningPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigning = new I2PSigningPublicKey( PrivateSigning );

            var CertificateEd25519 = new I2PCertificate( I2PSigningKey.SigningKeyTypes.EdDSA_SHA512_Ed25519 );
            PrivateSigningEd25519 = new I2PSigningPrivateKey( CertificateEd25519 );
            PublicSigningEd25519 = new I2PSigningPublicKey( PrivateSigningEd25519 );

            Me = new I2PRouterIdentity( Public, new I2PSigningPublicKey( new BigInteger( "12" ), I2PKeyType.DefaultSigningKeyCert ) );
        }

        [Test]
        public void TestSimpleDatabaseStoreCreation()
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

            var data = dbsm.CreateHeader16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.RouterInfo.Options["One"] == "1" );
            Assert.IsTrue( rdsm.RouterInfo.Options["2"] == "Two" );
            Assert.IsTrue( rdsm.RouterInfo.VerifySignature() );
        }

        [Test]
        public void TestSimpleDatabaseHeader5StoreCreation()
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

            var data = dbsm.CreateHeader16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.RouterInfo.Options["One"] == "1" );
            Assert.IsTrue( rdsm.RouterInfo.Options["2"] == "Two" );
            Assert.IsTrue( rdsm.RouterInfo.VerifySignature() );
        }

        [Test]
        public void TestSimpleDatabaseStoreLeaseSetCreation()
        {
            var leases = new List<I2PLease>();

            for ( int i = 0; i < 5; ++i )
            {
                leases.Add( new I2PLease( 
                        new I2PIdentHash( true ), 
                        new I2PTunnelId() ) );
            }

            var ls = new I2PLeaseSet( new I2PDestination( Public, PublicSigning ), leases, Public, PublicSigning, PrivateSigning );

            var dbsm = new DatabaseStoreMessage( ls );

            var data = dbsm.CreateHeader16.HeaderAndPayload.Clone();

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.LeaseSet.Leases.Count() == 5 );

            Assert.IsTrue( BufUtils.Equal( ls.Destination.ToByteArray(), rdsm.LeaseSet.Destination.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicKey.ToByteArray(), rdsm.LeaseSet.PublicKeys.First().ToByteArray() ) );
            //Assert.IsTrue( BufUtils.Equal( ls.PublicSigningKey.ToByteArray(), rdsm.LeaseSet.PublicSigningKey.ToByteArray() ) );

/*
            var rdsmlsar = rdsm.LeaseSet.Leases.ToArray();
            var lsar = ls.Leases.ToArray();

            // Order should be maintained
            for ( int i = 0; i < 5; ++i )
            {
                Assert.IsTrue( 
                        BufUtils.Equal( 
                            lsar[i].ToByteArray(), 
                            rdsmlsar[i].ToByteArray() ) );
            }*/

            Assert.IsTrue( 
                    BufUtils.Equal( 
                        rdsm.LeaseSet.ToByteArray(), 
                        ls.ToByteArray() ) );

            //Assert.IsTrue( rdsm.LeaseSet.VerifySignature( PublicSigning ) );
        }

        [Test]
        public void TestSimpleDatabaseStoreLeaseSet2Creation()
        {
            var leasecount = 5;

            var leases = Enumerable
                    .Range( 0, leasecount )
                    .Select( i => new I2PLease2( 
                        new I2PIdentHash( true ), 
                        new I2PTunnelId() ) );

            var ls = new I2PLeaseSet2( 
                    new I2PDestination( Public, PublicSigning ),
                    leases,
                    new I2PPublicKey[] { Public },
                    PublicSigning,
                    PrivateSigning );

            var dbsm = new DatabaseStoreMessage( ls );

            var data = dbsm.CreateHeader16.HeaderAndPayload.Clone();

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.LeaseSet.Leases.Count() == leasecount );

            Assert.IsTrue( BufUtils.Equal( ls.Destination.ToByteArray(), rdsm.LeaseSet.Destination.ToByteArray() ) );
            Assert.IsTrue( 
                BufUtils.Equal( 
                    I2PHashSHA256.GetHash( ls.PublicKeys.Select( k => k.Key ).ToArray() ),
                    I2PHashSHA256.GetHash( rdsm.LeaseSet.PublicKeys.Select( k => k.Key ).ToArray() ) ) );
            //Assert.IsTrue( BufUtils.Equal( ls.PublicSigningKey.ToByteArray(), rdsm.LeaseSet.PublicSigningKey.ToByteArray() ) );

            var rdsmlsar = rdsm.LeaseSet.Leases.ToArray();
            var lsar = ls.Leases.ToArray();

            // Order should be maintained
            for ( int i = 0; i < leasecount; ++i )
            {
                Assert.IsTrue( lsar[i].TunnelGw == rdsmlsar[i].TunnelGw );
                Assert.IsTrue( lsar[i].TunnelId == rdsmlsar[i].TunnelId );
                Assert.IsTrue( lsar[i].Expire == rdsmlsar[i].Expire );
            }

            var rdsmb = rdsm.LeaseSet.ToByteArray();
            var lsb = ls.ToByteArray();
            Assert.IsTrue( BufUtils.Equal( rdsmb, lsb ) );

            //Assert.IsTrue( rdsm.LeaseSet.VerifySignature( PublicSigning ) );
        }

        [Test]
        public void TestSimpleDatabaseStoreLeaseSetEd25519Creation()
        {
            var leases = new List<I2PLease>();
            for ( int i = 0; i < 5; ++i )
            {
                leases.Add( 
                        new I2PLease( new I2PIdentHash( true ),
                        (uint)( ( i * 72 + 6 ) * i * 1314 + 5 ) % 40000,
                        new I2PDate( (ulong)I2PDate.Now + 5 * 60 * 1000 ) ) );
            }

            var ls = new I2PLeaseSet( new I2PDestination( Public, PublicSigningEd25519 ), leases, Public, PublicSigningEd25519, PrivateSigningEd25519 );

            var dbsm = new DatabaseStoreMessage( ls );

            var data = dbsm.CreateHeader16.HeaderAndPayload;

            var recreated = I2NPMessage.ReadHeader16( new BufRefLen( data ) );

            Assert.IsTrue( recreated.MessageType == I2NPMessage.MessageTypes.DatabaseStore );
            var rdsm = (DatabaseStoreMessage)recreated.Message;
            Assert.IsTrue( rdsm.LeaseSet.Leases.Count() == 5 );

            Assert.IsTrue( BufUtils.Equal( ls.Destination.ToByteArray(), rdsm.LeaseSet.Destination.ToByteArray() ) );
            Assert.IsTrue( BufUtils.Equal( ls.PublicKey.ToByteArray(), rdsm.LeaseSet.PublicKeys.First().ToByteArray() ) );
            //Assert.IsTrue( BufUtils.Equal( ls.PublicSigningKey.ToByteArray(), rdsm.LeaseSet.PublicSigningKey.ToByteArray() ) );

            Assert.IsTrue( 
                    BufUtils.Equal( 
                        rdsm.LeaseSet.ToByteArray(), 
                        ls.ToByteArray() ) );
/*
            var rdsmlsar = rdsm.LeaseSet.Leases.ToArray();
            var lsar = ls.Leases.ToArray();

            for ( int i = 0; i < 5; ++i )
                Assert.IsTrue( BufUtils.Equal( lsar[i].ToByteArray(), rdsmlsar[i].ToByteArray() ) );

            Assert.IsTrue( rdsm.LeaseSet.VerifySignature( PublicSigningEd25519 ) );*/
        }

        [Test]
        public void VariableTunnelBuildMessageTest()
        {
            var msg = VariableTunnelBuildMessage.BuildInboundTunnel(
                new TunnelInfo(
                    new List<HopInfo>
                    {
                        new HopInfo( new I2PDestination( Public, PublicSigning ), BufUtils.RandomUint() ),
                        new HopInfo( new I2PDestination( Public, PublicSigning ), BufUtils.RandomUint() ),
                        new HopInfo( new I2PDestination( Public, PublicSigning ), BufUtils.RandomUint() ),
                    } )
                );

            var msgdata = msg.CreateHeader16.HeaderAndPayload;

            var msg2 = new VariableTunnelBuildMessage( new BufRefLen( msg.Payload ) );
            var msg2data = msg2.CreateHeader16.HeaderAndPayload;

            Assert.IsTrue( msgdata == msg2data );
        }
    }
}
