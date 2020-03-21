using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using Org.BouncyCastle.Math;
using System.Net;
using I2PCore.TransportLayer.SSU;
using I2PCore.SessionLayer;
using I2PCore.TransportLayer;
using I2PCore;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace I2PTests
{
    [TestFixture]
    public class TransportTest
    {
        public TransportTest()
        {
        }

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

        // This test does not work
        //[Test]
        public void TestSSU()
        {
            //Logging.LogToFile( "TestSSU.log" );

            RouterContext testcontext = new RouterContext( new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ) );
            testcontext.DefaultTCPPort = 2048 + BufUtils.RandomInt( 5000 );
            testcontext.DefaultUDPPort = 2048 + BufUtils.RandomInt( 5000 );

            var host = new SSUHost( testcontext, new FixedMTU() );
            host.AllowConnectToSelf = true;

            host.ConnectionCreated += host_ConnectionCreated;

            // Remote
            var dnsa = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.Where( a => a.AddressFamily == AddressFamily.InterNetwork ).FirstOrDefault();
            var addr = new I2PRouterAddress( dnsa, testcontext.UDPPort, 6, "SSU" );
            addr.Options["key"] = FreenetBase64.Encode( testcontext.IntroKey );

            RouterContext remotetestcontext = new RouterContext( new I2PCertificate( I2PSigningKey.SigningKeyTypes.DSA_SHA1 ) );
            remotetestcontext.DefaultTCPPort = testcontext.DefaultTCPPort + 5;
            remotetestcontext.DefaultUDPPort = testcontext.DefaultUDPPort + 5;

            var remotehost = new SSUHost( remotetestcontext, new FixedMTU() );
            remotehost.AllowConnectToSelf = true;
            var client = remotehost.AddSession( addr, testcontext.MyRouterIdentity );
            client.Connect();

            var data = new BufLen( BufUtils.RandomBytes( 30000 ) );

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
                Assert.IsTrue( ( (DataMessage)DataReceived.Random().Message ).DataMessagePayload ==
                    new BufLen( data ) );
            }

            System.Threading.Thread.Sleep( 500 );

            client.Terminate();

            System.Threading.Thread.Sleep( 500 );

            host.Terminate();

            System.Threading.Thread.Sleep( 500 );
        }

        void host_ConnectionCreated( ITransport transport )
        {
            transport.DataBlockReceived += host_DataBlockReceived;
        }

        void host_DataBlockReceived( ITransport arg1, II2NPHeader arg2 )
        {
            lock ( DataReceived )
            {
                DataReceived.Add( arg2 );
            }
        }

        [Test]
        public void TestSSUFragmentation1()
        {
            var fragmenter = new DataFragmenter();

            var smalldata = new BufLen( new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, } );
            var smalldatamessage = new DataMessage( smalldata );

            var dest = new byte[MTUConfig.BufferSize];
            var start = new BufLen( dest );
            var writer = new BufRefLen( dest );

            var tosend = new ConcurrentQueue<II2NPHeader16>();
            tosend.Enqueue( smalldatamessage.CreateHeader16 );

            var sent = new LinkedList<II2NPHeader16>();
            foreach ( var one in tosend ) sent.AddLast( one );

            var sentdata = new LinkedList<BufLen>();

            while ( true )
            {
                var flagbuf = writer.ReadBufLen( 1 );
                var fragcountbuf = writer.ReadBufLen( 1 );

                var fragments = fragmenter.Send( writer, tosend );
                if ( fragments == 0 ) break;

                flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.WantReply;
                // no ACKs
                fragcountbuf[0] = (byte)fragments;

                sentdata.AddLast( new BufLen( start, 0, writer - start ) );
                dest = new byte[MTUConfig.BufferSize];
                start = new BufLen( dest );
                writer = new BufRefLen( dest );
            }

            var receivedmessages = new LinkedList<II2NPHeader16>();

            var defragmenter = new DataDefragmenter();
            foreach ( var frag in sentdata )
            {
                var datamsg = new SSUDataMessage( new BufRefLen( frag ), defragmenter );
                if ( datamsg.NewMessages != null )
                {
                    foreach( var msg in datamsg.NewMessages )
                    {
                        var i2npmsg = I2NPMessage.ReadHeader16( (BufRefLen)msg.GetPayload() );
                        receivedmessages.AddLast( i2npmsg );
                    }
                }
            }

            Assert.IsTrue( receivedmessages.Count == sent.Count );

            /*

            var st1 = FreenetBase64.Encode( sent.First.Value.Message.Payload );
            var st2 = FreenetBase64.Encode( receivedmessages.First.Value.Message.Payload );

            Console.WriteLine( st1 );
            Console.WriteLine( st2 );

            Console.WriteLine( sent.First.Value.Message );
            Console.WriteLine( receivedmessages.First.Value.Message );

            Console.WriteLine( sent.First.Value.Message.Payload.ToString( "I500" ) );
            Console.WriteLine( receivedmessages.First.Value.Message.Payload.ToString( "I500" ) );
            */          

            foreach ( var sentmsg in sent )
            {
                Assert.IsTrue( receivedmessages.Any( m => m.Message.Payload == 
                    sentmsg.Message.Payload ) );
            }
        }

        [Test]
        public void TestSSUFragmentation()
        {
            var fragmenter = new DataFragmenter();

            var smalldata = new BufLen( BufUtils.RandomBytes( 30 ) );
            var smalldatamessage = new DataMessage( smalldata );

            var data = new BufLen( BufUtils.RandomBytes( 30000 ) );
            var datamessage = new DataMessage( data );

            var data2 = new BufLen( BufUtils.RandomBytes( 30000 ) );
            var datamessage2 = new DataMessage( data2 );

            var dest = new byte[MTUConfig.BufferSize];
            var start = new BufLen( dest );
            var writer = new BufRefLen( dest );

            var tosend = new ConcurrentQueue<II2NPHeader16>();
            tosend.Enqueue( smalldatamessage.CreateHeader16 );
            tosend.Enqueue( datamessage.CreateHeader16 );
            tosend.Enqueue( datamessage2.CreateHeader16 );

            var sent = new LinkedList<II2NPHeader16>();
            foreach ( var one in tosend ) sent.AddLast( one );

            var sentdata = new LinkedList<BufLen>();

            while ( true )
            {
                var flagbuf = writer.ReadBufLen( 1 );
                var fragcountbuf = writer.ReadBufLen( 1 );

                var fragments = fragmenter.Send( writer, tosend );
                if ( fragments == 0 ) break;

                flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.WantReply;
                // no ACKs
                fragcountbuf[0] = (byte)fragments;

                sentdata.AddLast( new BufLen( start, 0, writer - start ) );
                dest = new byte[MTUConfig.BufferSize];
                start = new BufLen( dest );
                writer = new BufRefLen( dest );
            }

            var receivedmessages = new LinkedList<II2NPHeader16>();

            var defragmenter = new DataDefragmenter();
            foreach ( var frag in sentdata )
            {
                var datamsg = new SSUDataMessage( new BufRefLen( frag ), defragmenter );
                if ( datamsg.NewMessages != null )
                {
                    foreach ( var msg in datamsg.NewMessages )
                    {
                        var i2npmsg = I2NPMessage.ReadHeader16( (BufRefLen)msg.GetPayload() );
                        receivedmessages.AddLast( i2npmsg );
                    }
                }
            }

            Assert.IsTrue( receivedmessages.Count == sent.Count );

            foreach ( var sentmsg in sent )
            {
                Assert.IsTrue( receivedmessages.Any( m => m.Message.Payload ==
                    sentmsg.Message.Payload ) );
            }
        }

        [Test]
        public void TestSSUOutOfOrderFragmentation()
        {
            var fragmenter = new DataFragmenter();

            var smalldata = new BufLen( BufUtils.RandomBytes( 4 + BufUtils.RandomInt( 4 ) ) );
            var smalldatamessage = new DataMessage( smalldata );

            var smalldata1 = new BufLen( BufUtils.RandomBytes( 40 + BufUtils.RandomInt( 14 ) ) );
            var smalldatamessage1 = new DataMessage( smalldata1 );

            var smalldata2 = new BufLen( BufUtils.RandomBytes( 130 + BufUtils.RandomInt( 39 ) ) );
            var smalldatamessage2 = new DataMessage( smalldata2 );

            var smalldata3 = new BufLen( BufUtils.RandomBytes( 770 + BufUtils.RandomInt( 220 ) ) );
            var smalldatamessage3 = new DataMessage( smalldata3 );

            var data = new BufLen( BufUtils.RandomBytes( 30000 + BufUtils.RandomInt( 30 ) ) );
            var datamessage = new DataMessage( data );

            var data2 = new BufLen( BufUtils.RandomBytes( 20000 + BufUtils.RandomInt( 1040 ) ) );
            var datamessage2 = new DataMessage( data2 );

            var dest = new byte[MTUConfig.BufferSize];
            var start = new BufLen( dest );
            var writer = new BufRefLen( dest );

            var tosend = new LinkedList<II2NPHeader16>();
            tosend.AddLast( smalldatamessage.CreateHeader16 );
            tosend.AddLast( datamessage.CreateHeader16 );
            tosend.AddLast( smalldatamessage1.CreateHeader16 );
            tosend.AddLast( smalldatamessage2.CreateHeader16 );
            tosend.AddLast( smalldatamessage3.CreateHeader16 );
            tosend.AddLast( datamessage2.CreateHeader16 );

            var tosendshuffle = tosend.Shuffle();
            var tosendshuffled = new ConcurrentQueue<II2NPHeader16>( tosendshuffle );

            var sent = new LinkedList<II2NPHeader16>();
            foreach ( var one in tosend ) sent.AddLast( one );

            var sentdata = new LinkedList<BufLen>();

            while ( true )
            {
                var flagbuf = writer.ReadBufLen( 1 );
                var fragcountbuf = writer.ReadBufLen( 1 );

                var fragments = fragmenter.Send( writer, tosendshuffled );
                if ( fragments == 0 ) break;

                flagbuf[0] |= (byte)SSUDataMessage.DataMessageFlags.WantReply;
                // no ACKs
                fragcountbuf[0] = (byte)fragments;

                sentdata.AddLast( new BufLen( start, 0, writer - start ) );
                dest = new byte[MTUConfig.BufferSize];
                start = new BufLen( dest );
                writer = new BufRefLen( dest );
            }

            var shuffled = sentdata.Shuffle();

            var receivedmessages = new LinkedList<II2NPHeader16>();

            var defragmenter = new DataDefragmenter();
            foreach ( var frag in shuffled )
            {
                var datamsg = new SSUDataMessage( new BufRefLen( frag ), defragmenter );
                if ( datamsg.NewMessages != null )
                {
                    foreach ( var msg in datamsg.NewMessages )
                    {
                        var i2npmsg = I2NPMessage.ReadHeader16( (BufRefLen)msg.GetPayload() );
                        receivedmessages.AddLast( i2npmsg );
                    }
                }
            }

            Assert.IsTrue( receivedmessages.Count == sent.Count );

            foreach ( var sentmsg in sent )
            {
                Assert.IsTrue( receivedmessages.SingleOrDefault( m => ( (DataMessage)m.Message ).Payload ==
                    ( (DataMessage)sentmsg.Message ).Payload ) != null );
            }
        }
    }
}
 