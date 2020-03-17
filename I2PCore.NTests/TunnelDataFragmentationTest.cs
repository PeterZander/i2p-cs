using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.Utils;

namespace I2PTests
{
    [TestFixture]
    public class TunnelDataFragmentationTest
    {
        [Test]
        public void MakeAndReadFragment()
        {
            var arec = new DatabaseLookupMessage(
                new I2PIdentHash( true ),
                new I2PIdentHash( true ),
                DatabaseLookupMessage.LookupTypes.Normal );

            var msg = new TunnelMessageRouter(
                arec,
                new I2PIdentHash( true ) );

            var refmsgdata = msg.Message.CreateHeader16.HeaderAndPayload;

            var fragments = TunnelDataMessage.MakeFragments(
                new TunnelMessage[] { msg },
                BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( fragments.Shuffle().ToArray(), out var _ );

            foreach ( var rmsg in recvtmsgs )
            {
                var rmsgdata = rmsg.Message.CreateHeader16.HeaderAndPayload;
                Assert.IsTrue( msg.Delivery == rmsg.Delivery );
                Assert.IsTrue( refmsgdata == rmsgdata );
            }
        }

        [Test]
        public void MakeAndReadFragmentLarge()
        {
            var arec = new DataMessage( new BufLen( BufUtils.RandomBytes( 2048 ) ) );

            var msg = new TunnelMessageRouter(
                arec,
                new I2PIdentHash( true ) );

            var refmsgdata = msg.Message.CreateHeader16.HeaderAndPayload;

            var fragments = TunnelDataMessage.MakeFragments(
                new TunnelMessage[] { msg },
                BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( fragments.Shuffle().ToArray(), out var _ );

            foreach ( var rmsg in recvtmsgs )
            {
                var rmsgdata = rmsg.Message.CreateHeader16.HeaderAndPayload;
                Assert.IsTrue( msg.Delivery == rmsg.Delivery );
                Assert.IsTrue( refmsgdata == rmsgdata );
            }
        }

        [Test]
        public void MakeAndReadFragments5()
        {
            var origmsgs = new List<TunnelMessage>();
            
            for ( int i = 0; i < 5; ++i )
            {
                var adatarec = new DataMessage( new BufLen( BufUtils.RandomBytes( 12 ) ) );

                var amsg = new TunnelMessageRouter(
                    adatarec,
                    new I2PIdentHash( true ) );

                origmsgs.Add( amsg );
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( msgs.Shuffle(), out var _ );

            Assert.IsTrue( origmsgs.All( o => recvtmsgs.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );
        }

        [Test]
        public void MakeAndReadFragments5_2()
        {
            var origmsgs = new List<TunnelMessage>();
            for ( int i = 0; i < 5; ++i )
            {
                var adatarec = new DataMessage( new BufLen( BufUtils.RandomBytes( 2048 ) ) );

                var amsg = new TunnelMessageRouter(
                    adatarec,
                    new I2PIdentHash( true ) );

                origmsgs.Add( amsg );
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = msgs
                    .Chunk( a => 2 )
                    .Shuffle()
                    .Select( m => mkmsg.Process( m, out var _ ) )
                    .SelectMany( b => b );

            Assert.IsTrue( origmsgs.All( o => recvtmsgs.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );
        }

        [Test]
        public void MakeAndReadFragments5Chunked()
        {
            var origmsgs = new List<TunnelMessage>();

            for ( int i = 0; i < 5; ++i )
            {
                var adatarec = new DataMessage( new BufLen( BufUtils.RandomBytes( 2048 ) ) );

                var amsg = new TunnelMessageRouter(
                    adatarec,
                    new I2PIdentHash( true ) );

                origmsgs.Add( amsg );
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( 
                        msgs.Chunk( a => 2 + BufUtils.RandomInt ( 2 ) )
                                .Shuffle()
                                .SelectMany( c => c )
                                .Shuffle(), out var _ );

            Assert.IsTrue( origmsgs.All( o => recvtmsgs.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );

            var msgs2 = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );
            recvtmsgs = mkmsg.Process(
                        msgs2.Chunk( a => 1 + BufUtils.RandomInt( 2 ) )
                                .Shuffle()
                                .ToArray()
                                .SelectMany( c => c )
                                .ToArray()
                                .Skip( 1 )
                                .Shuffle(), out var _ );

            Assert.IsFalse( origmsgs.All( o => recvtmsgs.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );
        }

        [Test]
        public void MakeAndReadFragments100()
        {
            var origmsgs = new List<TunnelMessage>();

            for ( int i = 0; i < 100; ++i )
            {
                switch ( BufUtils.RandomInt( 3 ) )
                {
                    case 0:
                        var adatarec = new DataMessage( new BufLen( BufUtils.RandomBytes( 2048 ) ) );

                        origmsgs.Add( new TunnelMessageTunnel(
                            adatarec,
                            new I2PIdentHash( true ),
                            BufUtils.RandomUint() ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage(
                            new I2PIdentHash( true ),
                            new I2PIdentHash( true ),
                            DatabaseLookupMessage.LookupTypes.Normal );

                        origmsgs.Add( new TunnelMessageRouter(
                            arec,
                            new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage(
                            new BufLen(
                                BufUtils.RandomBytes( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec2 ) );
                        break;
                }
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );
            var mkmsg = new TunnelDataFragmentReassembly();
            var chunks = msgs
                            .Shuffle()
                            .ToArray()
                            .Chunk( a => 1 + BufUtils.RandomInt( 10 ) )
                            .Shuffle();

            var recvtmsgs = chunks.SelectMany( c => mkmsg.Process( c, out var _ ) ).ToArray();

            Assert.IsTrue( origmsgs.All( o => recvtmsgs.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );

            var mkmsg2 = new TunnelDataFragmentReassembly();
            var chunks2 = msgs
                            .Shuffle()
                            .ToArray()
                            .Skip( 1 )
                            .Chunk( a => 1 + BufUtils.RandomInt( 10 ) )
                            .Shuffle();

            var recvtmsgs2 = chunks2.SelectMany( c => mkmsg2.Process( c, out var _ ) ).ToArray();
            Assert.IsFalse( origmsgs.All( o => recvtmsgs2.Any( m =>
                    m.Delivery == o.Delivery
                    && m.Message.CreateHeader16.HeaderAndPayload == o.Message.CreateHeader16.HeaderAndPayload
                ) ) );
        }

        [Test]
        public void MakeAndReadFragmentsWithSerialize()
        {
            var origmsgs = new List<TunnelMessage>();

            for ( int i = 0; i < 200; ++i )
            {
                switch ( BufUtils.RandomInt( 3 ) )
                {
                    case 0:
                        var adatarec = new DataMessage( 
                            new BufLen( 
                                BufUtils.RandomBytes( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage(
                            new I2PIdentHash( true ),
                            new I2PIdentHash( true ),
                            DatabaseLookupMessage.LookupTypes.RouterInfo );

                        origmsgs.Add( new TunnelMessageRouter( 
                            arec, 
                            new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage( 
                            new BufLen( 
                                BufUtils.RandomBytes( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageTunnel( adatarec2,
                            new I2PIdentHash( true ),
                            BufUtils.RandomUint() ) );
                        break;
                }
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );
            var recvlist = new List<TunnelDataMessage>();

            foreach ( var msg in msgs )
            {
                recvlist.Add( (TunnelDataMessage)I2NPMessage.ReadHeader16( 
                    new BufRefLen( msg.CreateHeader16.HeaderAndPayload ) ).Message );
            }

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( recvlist, out var _ );

            foreach ( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m => 
                    m.Delivery == rmsg.Delivery &&
                    m.Message.CreateHeader16.HeaderAndPayload == rmsg.Message.CreateHeader16.HeaderAndPayload 
                    ) != null );
            }
        }
    }
}
