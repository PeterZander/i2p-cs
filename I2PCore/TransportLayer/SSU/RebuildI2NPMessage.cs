using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
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
                if ( Fragments.Count <= frag.FragmentNumber )
                {
                    Fragments.AddRange( new DataFragment[frag.FragmentNumber - Fragments.Count + 1] );
                }
                Fragments[frag.FragmentNumber] = frag;

#if LOG_MUCH_TRANSPORT
                Logging.LogDebugData( "SSU received fragment " + frag.FragmentNumber.ToString() + " of message " + frag.MessageId.ToString() +
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

            const int h5inh16offset = 11;
            var result = new BufLen( new byte[h5inh16offset + messagesize] );

            var writer = new BufRefLen( result, h5inh16offset );
            foreach ( var onef in Fragments ) writer.Write( onef.Data );

            // Fake a I2NP16 header
            var exp = new I2PDate( SSUHost.SSUDateTime( result.PeekFlip32( 1 + h5inh16offset ) ) );
            result[0] = result[h5inh16offset];
            result.PokeFlip64( (ulong)exp, 5 );
            result.PokeFlip16( (ushort)( messagesize - 5 ), 13 );
            // Not wasting time on faking checksum

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

#if LOG_MUCH_TRANSPORT
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
