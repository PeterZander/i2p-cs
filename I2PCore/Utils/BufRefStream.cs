using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace I2PCore.Utils
{
    public class BufRefStream : Stream
    {
        long PositionField = 0;
        long LengthField = 0;

        LinkedList<BufRefLen> Bufs = new LinkedList<BufRefLen>();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => LengthField;

        public override long Position { get => PositionField; set => throw new NotImplementedException(); }

        public override void Flush()
        {
        }

        public override int Read( byte[] buffer, int offset, int count )
        {
            if ( Bufs.Count == 0 ) return 0;

            int result = 0;
            while ( Bufs.Count > 0 && count > 0 )
            {
                var toread = Math.Min( count, Bufs.First.Value.Length );
                Bufs.First.Value.Read( buffer, offset, toread );
                PositionField += toread;
                count -= toread;
                offset += toread;
                result += toread;

                if ( Bufs.First.Value.Length == 0 ) Bufs.RemoveFirst();
            }

            return result;
        }

        public byte[] ToArray()
        {
            var len = (int)( Length - Position );
            var result = new byte[len];
            Read( result, 0, len );
            return result;
        }

        public override long Seek( long offset, SeekOrigin origin )
        {
            throw new NotImplementedException();
        }

        public override void SetLength( long value )
        {
            throw new NotImplementedException();
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            var newbuf = new BufRefLen( buffer, offset, count );
            Bufs.AddLast( newbuf );
            LengthField += newbuf.Length;
        }
        public void Write( BufRefStream src )
        {
            foreach ( var one in src.Bufs )
            {
                Bufs.AddLast( one );
                LengthField += one.Length;
            }
        }
        public void Write( byte b )
        {
            var newbuf = new BufRefLen( new byte[] { b } );
            Bufs.AddLast( newbuf );
            LengthField += newbuf.Length;
        }
        public void Write( byte[] buffer )
        {
            var newbuf = new BufRefLen( buffer );
            Bufs.AddLast( newbuf );
            LengthField += newbuf.Length;
        }

        public void Write( BufLen buf )
        {
            Bufs.AddLast( (BufRefLen)buf );
            LengthField += buf.Length;
        }

        public void Write( BufRefLen buf )
        {
            Bufs.AddLast( buf );
            LengthField += buf.Length;
        }

        public byte[] ToByteArray()
        {
            var result = new byte[Bufs.Sum( b => b.Length )];
            var rwriter = new BufRefLen( result );
            foreach( var b in Bufs )
            {
                rwriter.Write( b );
            }
            return result;
        }
    }
}
