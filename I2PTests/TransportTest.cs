using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using Org.BouncyCastle.Math;
using System.Net;
using I2PCore.Transport.SSU;
using I2PCore.Router;
using I2PCore.Transport;
using I2PCore;
using System.Net.Sockets;

namespace I2PTests
{
    /// <summary>
    /// Summary description for TransportTest
    /// </summary>
    [TestClass]
    public class TransportTest
    {
        public TransportTest()
        {
        }

        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        List<II2NPHeader> DataReceived = new List<II2NPHeader>();

        class FixedMTU : IMTUProvider
        {
            public MTUConfig GetMTU( IPEndPoint ep ) 
            {
                var result = new MTUConfig();
                result.MTU = 1484 - 28;
                result.MTUMax = 1484 - 28;
                result.MTUMin = 620 - 28;
                return result; 
            }

            public void MTUUsed( IPEndPoint ep, MTUConfig mtu ) { }
        }

        [TestMethod]
        public void TestSSU()
        {
            DebugUtils.LogToFile( "TestSSU.log" );

            RouterContext testcontext = new RouterContext( new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ) );
            testcontext.DefaultTCPPort = 2048 + BufUtils.RandomInt( 5000 );
            testcontext.DefaultUDPPort = 2048 + BufUtils.RandomInt( 5000 );

            var host = new SSUHost_Accessor( testcontext, new FixedMTU() );
            host.AllowConnectToSelf = true;

            host.add_ConnectionCreated( new Action<I2PCore.Transport.ITransport>( host_ConnectionCreated ) );

            // Remote
            var dnsa = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.Where( a => a.AddressFamily == AddressFamily.InterNetwork ).FirstOrDefault();
            var addr = new I2PRouterAddress( dnsa, testcontext.UDPPort, 6, "SSU" );
            addr.Options["key"] = FreenetBase64.Encode( testcontext.IntroKey );

            RouterContext remotetestcontext = new RouterContext( new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ) );
            remotetestcontext.DefaultTCPPort = testcontext.DefaultTCPPort + 5;
            remotetestcontext.DefaultUDPPort = testcontext.DefaultUDPPort + 5;

            var remotehost = new SSUHost_Accessor( remotetestcontext, new FixedMTU() );
            remotehost.AllowConnectToSelf = true;
            var client = remotehost.AddSession( addr, testcontext.MyRouterIdentity );
            client.Connect();

            var data = new BufLen( BufUtils.Random( 30000 ) );

            var messagecount = 900; // If the out queue is larger than 1000 msgs we start discarding them

            for ( int i = 0; i < messagecount; ++i )
            {
                client.Send( new DataMessage( data ) );
            }

            System.Threading.Thread.Sleep( 10000 );

            for ( int i = 0; i < messagecount; ++i )
            {
                if ( i % 10 == 0 ) System.Threading.Thread.Sleep( 100 );
                client.Send( new DataMessage( data ) );
            }

            var start = new TickCounter();
            while ( DataReceived.Count < 2 * messagecount )
            {
                if ( start.DeltaToNow.ToMinutes >= 1 )
                {
                    Assert.Fail( "Failed to receive sent data due to a timeout" );
                    break;
                }

                System.Threading.Thread.Sleep( 500 );
            }

            for ( int i = 0; i < 100; ++i )
            {
                Assert.IsTrue( ( (I2PCore.Tunnel.I2NP.Messages.DataMessage)DataReceived.Random().Message ).DataMessagePayload ==
                    new BufLen( data ) );
            }

            System.Threading.Thread.Sleep( 500 );

            client.Terminate();

            System.Threading.Thread.Sleep( 500 );

            host.Terminate();

            System.Threading.Thread.Sleep( 500 );
        }

        void host_ConnectionCreated( I2PCore.Transport.ITransport transport )
        {
            transport.DataBlockReceived += new Action<I2PCore.Transport.ITransport, II2NPHeader>( host_DataBlockReceived );
        }

        void host_DataBlockReceived( I2PCore.Transport.ITransport arg1, II2NPHeader arg2 )
        {
            lock ( DataReceived )
            {
                DataReceived.Add( arg2 );
            }
        }

        [TestMethod]
        public void TestSSUFragmentation()
        {
            var fragmenter = new DataFragmenter();

            var smalldata = new BufLen( BufUtils.Random( 30 ) );
            var smalldatamessage = new I2PCore.Tunnel.I2NP.Messages.DataMessage( smalldata );

            var data = new BufLen( BufUtils.Random( 30000 ) );
            var datamessage = new I2PCore.Tunnel.I2NP.Messages.DataMessage( data );

            var data2 = new BufLen( BufUtils.Random( 30000 ) );
            var datamessage2 = new I2PCore.Tunnel.I2NP.Messages.DataMessage( data2 );

            var dest = new byte[MTUConfig.BufferSize];
            var start = new BufLen( dest );
            var writer = new BufRefLen( dest );

            var tosend = new LinkedList<II2NPHeader5>();
            tosend.AddLast( smalldatamessage.Header5 );
            tosend.AddLast( datamessage.Header5 );
            tosend.AddLast( datamessage2.Header5 );

            var sent = new LinkedList<II2NPHeader5>();
            foreach ( var one in tosend ) sent.AddLast( one );

            var sentdata = new LinkedList<BufLen>();

            while ( true )
            {
                var flagbuf = writer.ReadBufLen( 1 );
                var fragcountbuf = writer.ReadBufLen( 1 );

                var fragments = fragmenter.Send( writer, tosend );
                if ( fragments == 0 ) break;

                flagbuf[0] |= (byte)I2PCore.Transport.SSU.SSUDataMessage.DataMessageFlags.WantReply;
                // no ACKs
                fragcountbuf[0] = (byte)fragments;

                sentdata.AddLast( new BufLen( start, 0, writer - start ) );
                dest = new byte[MTUConfig.BufferSize];
                start = new BufLen( dest );
                writer = new BufRefLen( dest );
            }

            var receivedmessages = new LinkedList<II2NPHeader5>();

            var defragmenter = new DataDefragmenter();
            foreach ( var frag in sentdata )
            {
                var datamsg = new I2PCore.Transport.SSU.SSUDataMessage( new BufRefLen( frag ), defragmenter );
                if ( datamsg.NewMessages != null )
                {
                    foreach( var msg in datamsg.NewMessages )
                    {
                        var i2npmsg = I2NPMessage.ReadHeader5( (BufRefLen)msg.GetPayload() );
                        receivedmessages.AddLast( i2npmsg );
                    }
                }
            }

            Assert.IsTrue( receivedmessages.Count == sent.Count );

            foreach ( var sentmsg in sent )
            {
                Assert.IsTrue( receivedmessages.SingleOrDefault( m => ( (I2PCore.Tunnel.I2NP.Messages.DataMessage)m.Message ).Payload == 
                    ( (I2PCore.Tunnel.I2NP.Messages.DataMessage)sentmsg.Message ).Payload ) != null );
            }
        }

        [TestMethod]
        public void TestSSUOutOfOrderFragmentation()
        {
            var fragmenter = new DataFragmenter();

            var smalldata = new BufLen( BufUtils.Random( 4 + BufUtils.RandomInt( 4 ) ) );
            var smalldatamessage = new I2PCore.Tunnel.I2NP.Messages.DataMessage( smalldata );

            var smalldata1 = new BufLen( BufUtils.Random( 40 + BufUtils.RandomInt( 14 ) ) );
            var smalldatamessage1 = new I2PCore.Tunnel.I2NP.Messages.DataMessage( smalldata1 );

            var smalldata2 = new BufLen( BufUtils.Random( 130 + BufUtils.RandomInt( 39 ) ) );
            var smalldatamessage2 = new I2PCore.Tunnel.I2NP.Messages.DataMessage( smalldata2 );

            var smalldata3 = new BufLen( BufUtils.Random( 770 + BufUtils.RandomInt( 220 ) ) );
            var smalldatamessage3 = new I2PCore.Tunnel.I2NP.Messages.DataMessage( smalldata3 );

            var data = new BufLen( BufUtils.Random( 30000 + BufUtils.RandomInt( 30 ) ) );
            var datamessage = new I2PCore.Tunnel.I2NP.Messages.DataMessage( data );

            var data2 = new BufLen( BufUtils.Random( 20000 + BufUtils.RandomInt( 1040 ) ) );
            var datamessage2 = new I2PCore.Tunnel.I2NP.Messages.DataMessage( data2 );

            var dest = new byte[MTUConfig.BufferSize];
            var start = new BufLen( dest );
            var writer = new BufRefLen( dest );

            var tosend = new LinkedList<II2NPHeader5>();
            tosend.AddLast( smalldatamessage.Header5 );
            tosend.AddLast( datamessage.Header5 );
            tosend.AddLast( smalldatamessage1.Header5 );
            tosend.AddLast( smalldatamessage2.Header5 );
            tosend.AddLast( smalldatamessage3.Header5 );
            tosend.AddLast( datamessage2.Header5 );

            var tosendshuffle = tosend.Shuffle();
            var tosendshuffled = new LinkedList<II2NPHeader5>( tosendshuffle );

            var sent = new LinkedList<II2NPHeader5>();
            foreach ( var one in tosend ) sent.AddLast( one );

            var sentdata = new LinkedList<BufLen>();

            while ( true )
            {
                var flagbuf = writer.ReadBufLen( 1 );
                var fragcountbuf = writer.ReadBufLen( 1 );

                var fragments = fragmenter.Send( writer, tosendshuffled );
                if ( fragments == 0 ) break;

                flagbuf[0] |= (byte)I2PCore.Transport.SSU.SSUDataMessage.DataMessageFlags.WantReply;
                // no ACKs
                fragcountbuf[0] = (byte)fragments;

                sentdata.AddLast( new BufLen( start, 0, writer - start ) );
                dest = new byte[MTUConfig.BufferSize];
                start = new BufLen( dest );
                writer = new BufRefLen( dest );
            }

            var shuffled = sentdata.Shuffle();

            var receivedmessages = new LinkedList<II2NPHeader5>();

            var defragmenter = new DataDefragmenter();
            foreach ( var frag in shuffled )
            {
                var datamsg = new I2PCore.Transport.SSU.SSUDataMessage( new BufRefLen( frag ), defragmenter );
                if ( datamsg.NewMessages != null )
                {
                    foreach ( var msg in datamsg.NewMessages )
                    {
                        var i2npmsg = I2NPMessage.ReadHeader5( (BufRefLen)msg.GetPayload() );
                        receivedmessages.AddLast( i2npmsg );
                    }
                }
            }

            Assert.IsTrue( receivedmessages.Count == sent.Count );

            foreach ( var sentmsg in sent )
            {
                Assert.IsTrue( receivedmessages.SingleOrDefault( m => ( (I2PCore.Tunnel.I2NP.Messages.DataMessage)m.Message ).Payload ==
                    ( (I2PCore.Tunnel.I2NP.Messages.DataMessage)sentmsg.Message ).Payload ) != null );
            }
        }
    }
}