using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class GarlicAESBlock: I2PType
    {
        public BufLen TagCount;
        public List<BufLen> Tags = new List<BufLen>();
        public BufLen PayloadSize;
        public BufLen PayloadHash;
        public BufLen Flag;
        public BufLen NewSessionKey;
        public BufLen Payload;
        public BufLen Padding;

        public BufLen DataBuf;

        public GarlicAESBlock( BufRefLen reader )
        {
            var start = new BufLen( reader );

            TagCount = reader.ReadBufLen( 2 );
            var tags = TagCount.PeekFlip16( 0 );
            if ( tags > 0 )
            {
                if ( tags * I2PSessionTag.TagLength > start.Length ) throw new ArgumentException( "GarlicAESBlock: Not enough data for the tags supplied." );
                for ( int i = 0; i < tags; ++i ) Tags.Add( reader.ReadBufLen( I2PSessionTag.TagLength ) );
            }
            PayloadSize = reader.ReadBufLen( 4 );
            PayloadHash = reader.ReadBufLen( 32 );
            Flag = reader.ReadBufLen( 1 );
            if ( Flag[0] != 0 ) NewSessionKey = reader.ReadBufLen( 32 );
            var pllen = PayloadSize.PeekFlip32( 0 );
            if ( pllen > reader.Length ) throw new ArgumentException( "GarlicAESBlock: Not enough data payload supplied." );
            Payload = reader.ReadBufLen( (int)pllen );
            Padding = reader.ReadBufLen( BufUtils.Get16BytePadding( reader - start ) );
        }

        public GarlicAESBlock( 
            BufRefLen reader,
            IList<I2PSessionTag> tags, 
            I2PSessionKey newsessionkey,
            BufRefLen payload )
        {
            var start = new BufLen( reader );

            // Allocate
            TagCount = reader.ReadBufLen( 2 );
            if ( tags != null ) for( int i = 0; i < tags.Count; ++i ) Tags.Add( reader.ReadBufLen( I2PSessionTag.TagLength ) );
            PayloadSize = reader.ReadBufLen( 4 );
            PayloadHash = reader.ReadBufLen( 32 );
            Flag = reader.ReadBufLen( 1 );
            if ( newsessionkey != null ) reader.ReadBufLen( 32 );
            var pllen = Math.Min( reader.Length, payload.Length );
            Payload = reader.ReadBufLen( pllen );
            Padding = reader.ReadBufLen( BufUtils.Get16BytePadding( reader - start ) );

            // Write
            TagCount.PokeFlip16( (ushort)( tags == null ? 0 : tags.Count ), 0 );
            if ( tags != null ) for ( int i = 0; i < tags.Count; ++i ) Tags[i].Poke( tags[i].Value, 0 );
            Flag[0] = (byte)( newsessionkey != null ? 0x01 : 0 );
            if ( newsessionkey != null ) NewSessionKey.Poke( newsessionkey.Key, 0 );
            Payload.Poke( new BufLen( payload, 0, pllen ), 0 );
            payload.Seek( pllen );
            PayloadSize.PokeFlip32( (uint)pllen, 0 );
            PayloadHash.Poke( I2PHashSHA256.GetHash( Payload ), 0 );
            Padding.Randomize();

            DataBuf = new BufLen( start, 0, reader - start );
        }

        public bool VerifyPayloadHash()
        {
            return PayloadHash == new BufLen( I2PHashSHA256.GetHash( Payload ) );
        }

        public int Length { get { return DataBuf.Length; } }

        public void Write( BufRefStream dest )
        {
            DataBuf.WriteTo( dest );
        }
    }
}
