using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;
using System.Collections;

namespace I2PCore.Transport.SSU
{
    public class FragmentedMessage
    {
        internal const int NumberOfSendRetries = 4;
        internal const int SendRetriesMTUDecrease = 5;
        internal const int MillisecondsBetweenSendRetries = 3500;

        List<DataFragment> Fragments = new List<DataFragment>();

        public readonly TickCounter Created = TickCounter.Now;

        public int SendCount = 0;

        public readonly II2NPHeader5 Message;
        BufLen MessageBytes;
        public readonly uint MessageId;

        BufRefLen FragmentReader;

        public bool AllFragmentsAcked = false;

        public int FragmentCount() { return Fragments.Count(); }
        public int FragmentSendCount() { lock ( Fragments ) return Fragments.Sum( f => f.SendCount ); }

        public FragmentedMessage( II2NPHeader5 msg )
        {
            Message = msg;
            MessageBytes = new BufLen( msg.HeaderAndPayload );
            MessageId = BufUtils.RandomUint();

            FragmentReader = new BufRefLen( MessageBytes );
        }

        public bool AllFragmentsSent { get { return FragmentReader.Length == 0; } }

        public DataFragment Send( BufRefLen writer )
        {
            if ( AllFragmentsSent || writer.Length < 10 ) return null;

            var fragsize = Math.Min( FragmentReader.Length, writer.Length - 7 );

            // This would be nice, but it would limit the size of I2NP messages.
            //fragsize = Math.Min( ( Session.MTUMin / 4 ) * 3, fragsize ); // To make resends work with reduced MTU

            var newdata = FragmentReader.ReadBufLen( fragsize );
            var fragment = new DataFragment( newdata );
            fragment.MessageId = MessageId;
            fragment.IsLast = FragmentReader.Length == 0;
            lock ( Fragments )
            {
                fragment.FragmentNumber = (byte)Fragments.Count;
                Fragments.Add( fragment );
            }

#if LOG_ALL_TRANSPORT
            DebugUtils.LogDebug( () => string.Format( 
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

#if LOG_ALL_TRANSPORT
            StringBuilder bits = new StringBuilder();
            foreach ( var one in fragments ) bits.AppendFormat( "{0}0X{1:X2}", ( bits.Length != 0 ? ", " : "" ), one );
            int notacked;
            lock ( Fragments ) notacked = Fragments.Count( f => !f.Ack );
            DebugUtils.LogDebug( () => string.Format( "SSU received ACK {0} bits: {1}, not ACKed: {2} - {3}",
                MessageId, bits, notacked, BitmapACKStatusDebug() ) );
#endif
        }

#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
        internal string BitmapACKStatusDebug()
        {
            StringBuilder bits = new StringBuilder();
            lock ( Fragments ) foreach ( var one in Fragments ) bits.Insert( 0, ( one.Ack ? '1' : '0' ) );
            return bits.ToString();
        }
#endif
    }
}
