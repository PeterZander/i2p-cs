using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;
using I2PCore.Tunnel.I2NP.Messages;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class Garlic: I2PType
    {
        public BufLen Data;

        public List<GarlicClove> Cloves = new List<GarlicClove>();

        public Garlic( BufRefLen reader )
        {
            ParseData( reader );
        }

        public Garlic( params GarlicClove[] cloves )
            : this( new I2PDate( DateTime.UtcNow.AddMinutes( 5 ) ), cloves )
        {
        }

        public Garlic( I2PDate expiration, params GarlicClove[] cloves )
            : this( expiration, cloves.AsEnumerable() )
        {
        }

        public Garlic( IEnumerable<GarlicClove> cloves )
            : this( new I2PDate( DateTime.UtcNow.AddMinutes( 5 ) ), cloves )
        {
        }

        public Garlic( I2PDate expiration, IEnumerable<GarlicClove> cloves )
        {
            BufRefStream buf = new BufRefStream();
            buf.Write( (byte)cloves.Count() );
            foreach ( var clove in cloves ) clove.Write( buf );

            // Certificate
            buf.Write( new byte[] { 0, 0, 0 } );

            buf.Write( (BufRefLen)BufUtils.Flip32BL( BufUtils.RandomUint() ) );
            expiration.Write( buf );

            Data = new BufLen( buf.ToArray() );
            ParseData( new BufRefLen( Data ) );
        }

        void ParseData( BufRefLen reader )
        {
            var start = new BufLen( reader );

            var cloves = reader.Read8();
            for ( int i = 0; i < cloves; ++i )
            {
                Cloves.Add( new GarlicClove( reader ) );
            }
            reader.Seek( 3 + 4 + 8 ); // Garlic: Cert, MessageId, Expiration

            Data = new BufLen( start, 0, reader - start );
        }

        public override string ToString()
        {
            return string.Format( "Garlic: {0} cloves.", Cloves.Count );
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }
    }
}
