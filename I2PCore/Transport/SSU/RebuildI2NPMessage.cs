using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class RebuildI2NPMessage
    {
        List<DataFragment> Fragments = new List<DataFragment>();

        public readonly TickCounter Created = TickCounter.Now;
        public readonly uint MessageId;

        public bool FoundLast { get; protected set; }
        public bool AllFragmentsFound { get; protected set; }

        public TickCounter AckSent = TickCounter.MaxDelta;
        public int ExplicitAcksSent = 0;
        public int BitmapAcksSent = 0;

        public RebuildI2NPMessage( uint msgid )
        {
            MessageId = msgid;
        }

        public RebuildI2NPMessage Add( DataFragment frag )
        {
            lock ( Fragments )
            {
                if ( Fragments.Count <= frag.FragmentNumber ) Fragments.AddRange( new DataFragment[frag.FragmentNumber - Fragments.Count + 1] );
                Fragments[frag.FragmentNumber] = frag;

#if LOG_ALL_TRANSPORT
                DebugUtils.Log( "SSU received fragment " + frag.FragmentNumber.ToString() + " of message " + frag.MessageId.ToString() +
                    ". IsLast: " + frag.IsLast.ToString() );
#endif

                if ( frag.IsLast ) FoundLast = true;

                if ( FoundLast && !Fragments.Contains( null ) )
                {
                    AllFragmentsFound = true;

                    return this;
                }
            }

            return null;
        }

        public BufLen GetPayload()
        {
            if ( !AllFragmentsFound ) throw new Exception( "Cannot reassemble payload without all the fragments!" );

            var messagesize = Fragments.Sum( f => f.Data.Length );

            // Add 11 bytes to reserve space for a 16 byte I2NP header
            // I2NPHeader5 messages is only created here, so this ugliness is only used once.
            var buffer = new BufLen( new byte[11 + messagesize] ); 

            var result = new BufLen( buffer, 11 );
            var writer = new BufRefLen( result );
            foreach ( var onef in Fragments ) writer.Write( onef.Data );
            return result;
        }

        public int AckBitmapSize { get { return ( Fragments.Count - 1 ) / 7 + 1; } }

        public byte[] AckBitmap()
        {
            var bytes = AckBitmapSize;
            var result = new byte[bytes];

            var fragix = 0;
            for ( int i = 0; i < bytes; ++i )
            {
                byte bitm = 0;
                byte shiftbit = 0x01;
                for ( int j = 0; j < 7 && fragix < Fragments.Count; ++fragix, ++j, shiftbit <<= 1 )
                {
                    if ( Fragments[fragix] != null ) bitm |= shiftbit;
                }
                if ( i != bytes - 1 ) bitm |= 0x80;
                result[i] = bitm;
            }

            return result;
        }

#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
        internal string CurrentlyACKedBitmapDebug()
        {
            StringBuilder bits = new StringBuilder();
            lock ( Fragments ) foreach ( var one in Fragments ) bits.Insert( 0, ( one != null ? '1' : '0' ) );
            return bits.ToString();
        }
#endif
    }

    public class AckI2NPMessage : RebuildI2NPMessage
    {
        public AckI2NPMessage( uint msgid ) : base( msgid ) { AllFragmentsFound = true; FoundLast = true; }
    }
}
