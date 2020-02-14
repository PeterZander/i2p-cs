using System;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSU3Header: I2PType
    {
        public const string SU3_MAGIC_NUMBER = "I2Psu3";
        public enum SU3FileTypes : byte { Zip = 0x00 }
        public enum SU3ContentTypes : byte { SeedData = 0x03 }

        public I2PSU3Header( BufRef src )
        {
            Read( src );
        }

        public byte FileVersion { get; private set; }
        public ushort SignatureType { get; private set; }
        public ushort SignatureLength { get; private set; }
        public byte VersionLength { get; private set; }
        public BufLen Version { get; private set; }
        public byte SignerIdLength { get; private set; }
        public ulong ContentLength { get; private set; }
        public SU3FileTypes FileType { get; private set; }
        public SU3ContentTypes ContentType { get; private set; }
        public string SignerID { get; private set; }

        public void Read( BufRef reader )
        {
            // magic number and zero byte 6
            var magic = reader.ReadBufLen( 6 );
            _ = reader.Read8();
            var magicstr = magic.ToEncoding( Encoding.UTF8 );

            if ( magicstr != SU3_MAGIC_NUMBER )
            {
                throw new ArgumentException( "Not SU3 data." );
            }

            // su3 file format version
            FileVersion = reader.Read8();

            SignatureType = reader.ReadFlip16();
            SignatureLength = reader.ReadFlip16();
            _ = reader.Read8();
            VersionLength = reader.Read8();
            _ = reader.Read8();
            SignerIdLength = reader.Read8();
            ContentLength = reader.ReadFlip64();
            _ = reader.Read8();
            FileType = (SU3FileTypes)reader.Read8();
            _ = reader.Read8();
            ContentType = (SU3ContentTypes)reader.Read8();
            reader.Seek( 12 );
            Version = reader.ReadBufLen( VersionLength );
            SignerID = reader.ReadBufLen( SignerIdLength )
                .ToEncoding( Encoding.UTF8 ) ;
        }

        public void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
