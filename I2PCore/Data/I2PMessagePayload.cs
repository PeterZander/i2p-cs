using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Compression;
using System.IO;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PMessagePayload: I2PType
    {
        public ushort SessionId;
        public uint MessageId;
        public byte[] Payload;

        public void Compress( byte[] data )
        {
            using ( var ms = new MemoryStream() )
            {
                using ( var gs = new GZipStream( ms, CompressionMode.Compress ) )
                {
                    gs.Write( data, 0, data.Length );
                    gs.Flush();
                }
                Payload = ms.ToArray();
            }
        }

        public byte[] GetBytes
        {
            get
            {
                using ( var ms = new MemoryStream() )
                {
                    ms.Write( Payload, 0, Payload.Length );
                    ms.Position = 0;

                    using ( var gs = new GZipStream( ms, CompressionMode.Decompress ) )
                    {
                        var result = new List<byte>();
                        var buf = new byte[32768];

                        int len;
                        while ( ( len = gs.Read( buf, 0, buf.Length ) ) > 0 )
                        {
                            result.AddRange( buf.Take( len ) );
                        }

                        return result.ToArray();
                    }
                }
            }
        }

        public void Write( BufRefStream dest )
        {
            throw new NotImplementedException();
        }
    }
}
