using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PSessionKey : I2PType
    {
        public readonly BufLen Key;

        public I2PSessionKey()
        {
            Key = new BufLen( BufUtils.Random( 32 ) );
        }

        public I2PSessionKey( byte[] buf )
        {
            Key = new BufLen( buf, 0, 32 );
        }

        public I2PSessionKey( I2PSessionKey src )
        {
            Key = new BufLen( src.Key );
        }

        public I2PSessionKey( BufRef buf )
        {
            Key = buf.ReadBufLen( 32 );
        }

        public I2PSessionKey( BufLen buf )
        {
            Key = buf;
        }

        public void Write( List<byte> dest )
        {
            Key.WriteTo( dest );
        }
    }
}
