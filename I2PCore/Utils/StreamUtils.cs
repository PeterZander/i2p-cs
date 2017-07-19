using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace I2PCore.Utils
{
    public static class StreamUtils
    {
        public static string AppPath 
        { 
            get 
            {
				return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), "I2Pz" );
            } 
        }

        public delegate void CopyProgressCallback( long bytescopied );
        public static long CopyStream( Stream dest, Stream src, CopyProgressCallback cb, long maxlen, int bufsize )
        {
            long bytescopied = 0;
            byte[] buffer = new byte[bufsize];
            int read;
            while ( ( read = src.Read( buffer, 0, maxlen == -1 ? buffer.Length: (int)Math.Min( buffer.Length, maxlen - bytescopied ) ) ) > 0 )
            {
                dest.Write( buffer, 0, read );
                bytescopied += read;
                if ( cb != null )
                {
                    cb( bytescopied );
                }
            }
            return bytescopied;
        }

        public static byte[] Read( Stream src, long maxlen )
        {
            var result = new List<byte>();

            long bytescopied = 0;
            byte[] buffer = new byte[32768];
            int read;
            while ( ( read = src.Read( buffer, 0, maxlen == -1 ? buffer.Length : (int)Math.Min( buffer.Length, maxlen - bytescopied ) ) ) > 0 )
            {
                result.AddRange( buffer.Take( read ) );
                bytescopied += read;
            }
            return result.ToArray();
        }

		public static void Write( this Stream s, byte[] buf )
		{
			s.Write( buf, 0, buf.Length );
		}

        public static byte[] Read( Stream dest )
        {
            return Read( dest, -1 );
        }

        public static long CopyStream( Stream dest, Stream src, CopyProgressCallback cb, long maxlen )
        {
            return CopyStream( dest, src, cb, maxlen, 32768 );
        }

        public static long CopyStream( Stream dest, Stream src, CopyProgressCallback cb )
        {
            return CopyStream( dest, src, cb, -1, 32768 );
        }

        public static long CopyStream( Stream dest, Stream src )
        {
            return CopyStream( dest, src, null, -1, 32768 );
        }

        public static void WriteUInt8( this Stream dest, byte val )
        {
            dest.WriteByte( val );
        }

        public static void WriteUInt16( this Stream dest, ushort val )
        {
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( val >> 8 ) );
        }

        public static void WriteUInt32( this Stream dest, uint val )
        {
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( ( val & 0xff00 ) >> 8 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000 ) >> 16 ) );
            dest.WriteByte( (byte)( ( val & 0xff000000 ) >> 24 ) );
        }

        public static void WriteUInt64( this Stream dest, ulong val )
        {
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( ( val & 0xff00 ) >> 8 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000 ) >> 16 ) );
			dest.WriteByte( (byte)( ( val & 0xff000000 ) >> 24 ) );
			dest.WriteByte( (byte)( ( val & 0xff00000000 ) >> 32 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000000000 ) >> 40 ) );
			dest.WriteByte( (byte)( ( val & 0xff000000000000 ) >> 48 ) );
            dest.WriteByte( (byte)( ( val & 0xff00000000000000 ) >> 56 ) );
        }

		public static void WriteInt16( this Stream dest, short val )
		{
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( val >> 8 ) );
		}

		public static void WriteInt32( this Stream dest, int val )
		{
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( ( val & 0xff00 ) >> 8 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000 ) >> 16 ) );
			dest.WriteByte( (byte)( ( val & 0xff000000 ) >> 24 ) );
		}

		public static void WriteInt64( this Stream dest, long val )
		{
			dest.WriteByte( (byte)( val & 0xff ) );
			dest.WriteByte( (byte)( ( val & 0xff00 ) >> 8 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000 ) >> 16 ) );
			dest.WriteByte( (byte)( ( val & 0xff000000 ) >> 24 ) );
			dest.WriteByte( (byte)( ( val & 0xff00000000 ) >> 32 ) );
			dest.WriteByte( (byte)( ( val & 0xff0000000000 ) >> 40 ) );
			dest.WriteByte( (byte)( ( val & 0xff000000000000 ) >> 48 ) );
			dest.WriteByte( (byte)( ( (ulong)val & 0xff00000000000000 ) >> 56 ) );
		}

        public static byte ReadInt8( Stream src )
        {
            var v = src.ReadByte();
            if ( v == -1 ) throw new EndOfStreamEncounteredException();
			return (byte)v;
        }

        public static short ReadInt16( Stream src )
        {
			unchecked 
			{
				return (short)( ( ReadInt8( src ) | ( ReadInt8( src ) << 8 ) ) );
			}
        }

        public static int ReadInt32( Stream src )
        {
			unchecked 
			{
				return (int)( ReadUInt16( src ) | ( ReadUInt16( src ) << 16 ) );
			}
        }
			
		public static long ReadInt64( Stream src )
		{
			unchecked 
			{
				return ReadInt32( src ) | ( ReadInt32( src ) << 32 );
			}
		}

		public static ushort ReadUInt16( Stream src )
		{
			unchecked 
			{
				return (ushort)( ( ReadInt8( src ) | ( ReadInt8( src ) << 8 ) ) );
			}
		}

		public static uint ReadUInt32( Stream src )
		{
			unchecked 
			{
				return (uint)( ReadUInt16( src ) | ( ReadUInt16( src ) << 16 ) );
			}
		}

		public static ulong ReadUInt64( Stream src )
		{
			unchecked 
			{
				return ReadUInt32( src ) | ( ReadUInt32( src ) << 32 );
			}
		}

		public static int Read( this Stream s, byte[] dest, int offset, int len )
		{
			var result = 0;

			while ( len-- > 0 ) 
			{
				var l = s.ReadByte();
				if ( l <= 0 ) break;
				dest [offset++] = (byte)l;
				++result;
			}

			return result;
		}
	}
}
