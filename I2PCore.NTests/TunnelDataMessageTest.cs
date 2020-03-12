using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer;

namespace I2PTests
{
    [TestFixture]
    public class TunnelDataMessageTest
    {
        public TunnelDataMessageTest()
        {
        }

        [Test]
        public void TestSimpleTunnelDataCreation()
        {
            var smalldata = BufUtils.RandomBytes( 38 );

            var srcmsgs = new List<TunnelMessage>();
            srcmsgs.Add( new TunnelMessageLocal( new DeliveryStatusMessage( 1234 ) ) );
            var msgfrags = TunnelDataMessage.MakeFragments( srcmsgs, 0x3e5c );

            Assert.IsTrue( msgfrags.Count() == 1 );

            var firstmsg = msgfrags.First();
            var serialized = firstmsg.CreateHeader16.HeaderAndPayload;

            var recovered = I2NPMessage.ReadHeader16( new BufRefLen( serialized ) );

            var reassembler = new TunnelDataFragmentReassembly();
            var reassembledmsgs = reassembler.Process( 
                    new TunnelDataMessage[] 
                    { 
                        (TunnelDataMessage)recovered.Message 
                    }, out var _ );

            Assert.IsTrue( reassembledmsgs.Count() == 1 );

            var firstrecinstr = reassembledmsgs.First();
            Assert.IsTrue( firstrecinstr.Delivery == TunnelMessage.DeliveryTypes.Local );
            Assert.IsTrue( firstrecinstr.Message.MessageType == I2NPMessage.MessageTypes.DeliveryStatus );
            Assert.IsTrue( ( (DeliveryStatusMessage)firstrecinstr.Message ).StatusMessageId == 1234 );
        }

        [Test]
        public void TestSingleLargeTunnelDataCreation()
        {
            var sourcedata = new BufLen( BufUtils.RandomBytes( 9000 ) );

            var srcmsgs = new List<TunnelMessage>();
            srcmsgs.Add( new TunnelMessageTunnel( new DataMessage( sourcedata ), new I2PIdentHash( true ), 4242 ) );
            var msgfrags = TunnelDataMessage.MakeFragments( srcmsgs, 0x3e5c );

            Assert.IsTrue( msgfrags.Count() == 10 );

            var serbuf = new List<byte>();
            foreach ( var frag in msgfrags ) serbuf.AddRange( frag.CreateHeader16.HeaderAndPayload );
            var serbufarray = serbuf.ToArray();

            var reassembler = new TunnelDataFragmentReassembly();
            var reader = new BufRefLen( serbufarray );
            var readmsgs = new List<TunnelDataMessage>();
            while ( reader.Length > 0 ) readmsgs.Add( (TunnelDataMessage)( I2NPMessage.ReadHeader16( reader ) ).Message );

            var reassembledmsgs = reassembler.Process( readmsgs, out var _ );

            Assert.IsTrue( reassembledmsgs.Count() == 1 );
            Assert.IsTrue( reassembler.BufferedFragmentCount == 0 );

            var firstrecinstr = reassembledmsgs.First();
            Assert.IsTrue( firstrecinstr.Delivery == TunnelMessage.DeliveryTypes.Tunnel );
            Assert.IsTrue( ( (TunnelMessageTunnel)firstrecinstr ).Tunnel == 4242 );
            Assert.IsTrue( firstrecinstr.Message.MessageType == I2NPMessage.MessageTypes.Data );

            var datamsg = (DataMessage)firstrecinstr.Message;
            Assert.IsTrue( datamsg.DataMessagePayloadLength == sourcedata.Length );
            Assert.IsTrue( datamsg.DataMessagePayload == sourcedata );
        }
    }
}