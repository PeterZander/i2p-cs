using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class GarlicMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.Garlic; } }

        public EGGarlic CachedGarlic;
        public EGGarlic Garlic
        {
            get
            {
                if ( CachedGarlic == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedGarlic;
            }
        }

        public GarlicMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            UpdateCachedFields( reader );
            SetBuffer( start, reader );
        }

        public GarlicMessage( EGGarlic garlic )
        {
            // TODO: remove memory copying
            CachedGarlic = garlic;
            AllocateBuffer( garlic.Data.Length );
            Payload.Poke( garlic.Data, 0 );
        }

        void UpdateCachedFields( BufRef reader )
        {
            CachedGarlic = new EGGarlic( reader );
        }
    }
}
