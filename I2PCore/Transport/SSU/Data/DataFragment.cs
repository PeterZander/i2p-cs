using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class DataFragment
    {
        BufLen Buf;

        public BufLen MessageIdBuf { get { return new BufLen( Buf, 0, 4 ); } }
        public uint MessageId { get { return Buf.Peek32( 0 ); } set { Buf.Poke32( value, 0 ); } }

        public BufLen FragmentNumberBuf { get { return new BufLen( Buf, 4, 1 ); } }
        public byte FragmentNumber { get { return (byte)( Buf[4] >> 1 ); } set { Buf[4] = (byte)( ( Buf[4] & 0x01 ) | ( value << 1 ) ); } }
        public bool IsLast { get { return ( Buf[4] & 0x01 ) != 0; } set { Buf[4] = (byte)( ( Buf[4] & 0xfe ) | ( value ? 0x01 : 0x00 ) ); } }

        public BufLen FragmentDataSizeBuf { get { return new BufLen( Buf, 5, 2 ); } }
        public ushort FragmentDataSize { get { return (ushort)( Buf.PeekFlip16( 5 ) & 0x3fff ); } set { Buf.PokeFlip16( value, 5 ); } }

        public readonly BufLen Data;

        public int Size { get { return BufBase.SameBuffer( Buf, Data ) ? Buf.Length : Buf.Length + Data.Length; } }

        // Metadata for ACKing
        public bool Ack = false;
        public TickCounter LastSent = TickCounter.MaxDelta;
        public int SendCount = 0;

        // Parse received
        public DataFragment( BufRef reader )
        {
            Buf = reader.ReadBufLen( reader.PeekFlip16( 5 ) + 7 );
            Data = new BufLen( Buf, 7 );
        }

        // Create fragment for sending
        public DataFragment( BufLen data )
        {
            Buf = new BufLen( new byte[7] );
            FragmentDataSize = (ushort)data.Length;
            Data = data;
        }

        public void WriteTo( BufRef dest )
        {
            LastSent.SetNow();
            ++SendCount;
            if ( BufBase.SameBuffer( Buf, Data ) )
            {
                dest.Write( Buf );
            }
            else
            {
                dest.Write( Buf );
                dest.Write( Data );
            }
        }
    }
}
