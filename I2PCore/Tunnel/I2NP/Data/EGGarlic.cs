using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class EGGarlic: I2PType
    {
        public BufLen Data;
        public BufLen EGData { get { return new BufLen( Data, 4 ); } }

        public EGGarlic( BufRef reader )
        {
            Data = reader.ReadBufLen( (int)reader.PeekFlip32( 0 ) + 4 );
        }

        public int Length { get { return Data.Length; } }

        public void Write( List<byte> dest )
        {
            Data.WriteTo( dest );
        }
    }
}
