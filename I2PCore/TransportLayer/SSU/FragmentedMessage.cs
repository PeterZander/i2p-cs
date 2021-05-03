using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Collections;
using I2PCore.Data;

namespace I2PCore.TransportLayer.SSU
{
    public class FragmentedMessage
    {
        internal const int NumberOfSendRetries = 4;
        internal const int SendRetriesMTUDecrease = 5;
        internal const int MillisecondsBetweenSendRetries = 3500;

        List<DataFragment> Fragments = new List<DataFragment>();

        public readonly TickCounter Created = TickCounter.Now;

        public int SendCount = 0;

        public readonly uint MessageId;

        BufRefLen FragmentReader;

        public bool AllFragmentsAcked = false;

        public int FragmentCount() { return Fragments.Count(); }
        public int FragmentSendCount() { lock ( Fragments ) return Fragments.Sum( f => f.SendCount ); }

        public FragmentedMessage( II2NPHeader16 msg )
        {
            BufLen msgbytes;

            // Convert to 5 byte header
            var orig = new BufLen( msg.HeaderAndPayload );
            var msgtype = orig[0];
            var exp = new I2PDate( orig.PeekFlip64( 5 ) );

            msgbytes = new BufLen( orig, 16 - 5 );
            msgbytes[0] = msgtype;
            msgbytes.PokeFlip32( (uint)( (ulong)exp / 1000 ), 1 );

            MessageId = BufUtils.RandomUint();

            FragmentReader = new BufRefLen( msgbytes );
        }

        public bool AllFragmentsSent { get { return FragmentReader.Length == 0; } }

        public DataFragment Send( BufRefLen writer )
        {
            if ( AllFragmentsSent || writer.Length < 10 ) return null;

            var fragsize = Math.Min( FragmentReader.Length, writer.Length - 7 );

            // This would be nice, but it would limit the size of I2NP messages.
            //fragsize = Math.Min( ( Session.MTUMin / 4 ) * 3, fragsize ); // To make resends work with reduced MTU

            var newdata = FragmentReader.ReadBufLen( fragsize );
            var fragment = new DataFragment( newdata )
            {
                MessageId = MessageId,
                IsLast = FragmentReader.Length == 0
            };
            lock ( Fragments )
            {
                fragment.FragmentNumber = (byte)Fragments.Count;
                Fragments.Add( fragment );
            }

#if LOG_MUCH_TRANSPORT
            Logging.LogTransport( string.Format( 
                "SSU sending {0} fragment {1}, {2} bytes. IsLast: {3}",
                MessageId, fragment.FragmentNumber, fragment.Data.Length, fragment.IsLast ) );
#endif
            ++SendCount;
            fragment.WriteTo( writer );
            return fragment;
        }

        public bool HaveNotAcked { get { return !AllFragmentsAcked || !AllFragmentsSent; } }

        public IEnumerable<DataFragment> NotAckedFragments()
        {
            if ( AllFragmentsAcked ) yield break;

            lock ( Fragments )
            {
                foreach ( var frag in Fragments )
                {
                    if ( !frag.Ack && frag.SendCount < NumberOfSendRetries && 
                        frag.LastSent.DeltaToNowMilliseconds > MillisecondsBetweenSendRetries )
                    {
                        yield return frag;
                    }
                }
            }
        }

        public void GotAck()
        {
            AllFragmentsAcked = true;
        }

        public void GotAck( List<byte> fragments )
        {
            int fragnr = 0;
            lock ( Fragments )
            {
                foreach ( var one in fragments )
                {
                    byte bitmask = 0x01;
                    for ( int i = 0; i < 7; ++i, ++fragnr, bitmask <<= 1 )
                    {
                        var isrecved = ( one & bitmask ) != 0;
                        if ( fragnr < Fragments.Count ) Fragments[fragnr].Ack = isrecved;
                    }
                }
                AllFragmentsAcked |= Fragments.All( f => f.Ack );
            }

#if LOG_MUCH_TRANSPORT
            StringBuilder bits = new StringBuilder();
            foreach ( var one in fragments ) bits.AppendFormat( "{0}0X{1:X2}", ( bits.Length != 0 ? ", " : "" ), one );
            int notacked;
            lock ( Fragments ) notacked = Fragments.Count( f => !f.Ack );
            Logging.LogTransport( string.Format( "SSU received ACK {0} bits: {1}, not ACKed: {2} - {3}",
                MessageId, bits, notacked, BitmapACKStatusDebug() ) );
#endif
        }

#if LOG_MUCH_TRANSPORT
        internal string BitmapACKStatusDebug()
        {
            StringBuilder bits = new StringBuilder();
            lock ( Fragments ) foreach ( var one in Fragments ) bits.Insert( 0, ( one.Ack ? '1' : '0' ) );
            return bits.ToString();
        }
#endif

        public override string ToString()
        {
            return $"{MessageId}: {SendCount} / {FragmentCount()} {AllFragmentsSent} {AllFragmentsAcked}";
        }
    }
}
