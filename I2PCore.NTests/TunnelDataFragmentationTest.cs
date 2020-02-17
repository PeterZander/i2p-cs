using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Data;
using I2PCore.Tunnel;
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
                arec.Header16,
                new I2PIdentHash( true ) );

            var fragments = TunnelDataMessage.MakeFragments(
                new TunnelMessage[] { msg },
                BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( fragments );

            foreach ( var rmsg in recvtmsgs )
            {
                Assert.IsTrue(
                        msg.Delivery == rmsg.Delivery
                        && msg.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload );
            }
        }

        [Test]
        public void MakeAndReadFragments2()
        {
            var origmsgs = new List<TunnelMessage>();
            
            for ( int i = 0; i < 2; ++i )
            {
                var arec = new DatabaseLookupMessage( 
                    new I2PIdentHash( true ), 
                    new I2PIdentHash( true ), 
                    DatabaseLookupMessage.LookupTypes.Normal );

                var amsg = new TunnelMessageRouter(
                    arec.Header16,
                    new I2PIdentHash( true ) );

                origmsgs.Add( amsg );
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( msgs );

            foreach( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m =>
                        m.Delivery == rmsg.Delivery 
                        && m.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload
                    ) != null );
            }
        }

        [Test]
        public void MakeAndReadFragments200()
        {
            var origmsgs = new List<TunnelMessage>();

            for ( int i = 0; i < 200; ++i )
            {
                switch ( BufUtils.RandomInt( 3 ) )
                {
                    case 0:
                        var adatarec = new DataMessage( new BufLen( BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageTunnel(
                            adatarec.Header16,
                            new I2PIdentHash( true ),
                            BufUtils.RandomUint() ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage(
                            new I2PIdentHash( true ),
                            new I2PIdentHash( true ),
                            DatabaseLookupMessage.LookupTypes.Normal );

                        origmsgs.Add( new TunnelMessageRouter(
                            arec.Header16,
                            new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage(
                            new BufLen(
                                BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec2.Header16 ) );
                        break;
                }
            }

            var msgs = TunnelDataMessage.MakeFragments( origmsgs, BufUtils.RandomUint() );

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( msgs );

            foreach ( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m =>
                        m.Delivery == rmsg.Delivery
                        && m.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload
                    ) != null );
            }
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
                                BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageLocal( adatarec.Header16 ) );
                        break;

                    case 1:
                        var arec = new DatabaseLookupMessage(
                            new I2PIdentHash( true ),
                            new I2PIdentHash( true ),
                            DatabaseLookupMessage.LookupTypes.RouterInfo );

                        origmsgs.Add( new TunnelMessageRouter( 
                            arec.Header16, 
                            new I2PIdentHash( true ) ) );
                        break;

                    case 2:
                        var adatarec2 = new DataMessage( 
                            new BufLen( 
                                BufUtils.Random( 2048 + BufUtils.RandomInt( 1024 ) ) ) );

                        origmsgs.Add( new TunnelMessageTunnel( adatarec2.Header16,
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
                    new BufRefLen( msg.Header16.HeaderAndPayload ) ).Message );
            }

            var mkmsg = new TunnelDataFragmentReassembly();
            var recvtmsgs = mkmsg.Process( recvlist );

            foreach ( var rmsg in recvtmsgs )
            {
                Assert.IsTrue( origmsgs.SingleOrDefault( m => 
                    m.Delivery == rmsg.Delivery &&
                    m.Header.HeaderAndPayload == rmsg.Header.HeaderAndPayload 
                    ) != null );
            }
        }
    }
}
