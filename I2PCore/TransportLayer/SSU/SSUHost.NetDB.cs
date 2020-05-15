using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using I2PCore.Utils;
using System.Threading;
using I2PCore.SessionLayer;
using I2PCore.Data;
using I2PCore.TransportLayer.SSU.Data;

namespace I2PCore.TransportLayer.SSU
{
	public partial class SSUHost
	{
        List<KeyValuePair<string, string>> IntroducersInfo = new List<KeyValuePair<string, string>>();

        internal void NoIntroducers()
        {
            if ( IntroducersInfo.Any() )
            {
                IntroducersInfo = new List<KeyValuePair<string, string>>();
                UpdateRouterContext();
            }
        }

        internal void SetIntroducers( IEnumerable<IntroducerInfo> introducers )
        {
            if ( !introducers.Any() )
            {
                NoIntroducers();
                return;
            }

            var result = new List<KeyValuePair<string, string>>();
            var ix = 0;

            foreach ( var one in introducers )
            {
                result.Add( new KeyValuePair<string, string>( $"ihost{ix}", one.Host.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"iport{ix}", one.Port.ToString() ) );
                result.Add( new KeyValuePair<string, string>( $"ikey{ix}", FreenetBase64.Encode( one.IntroKey ) ) );
                result.Add( new KeyValuePair<string, string>( $"itag{ix}", one.IntroTag.ToString() ) );
                ++ix;
            }

            IntroducersInfo = result;

            UpdateRouterContext();
        }

        private void UpdateRouterContext()
        {
            var addr = new I2PRouterAddress( RouterContext.Inst.ExtAddress, RouterContext.Inst.UDPPort, 5, "SSU" );

            var ssucaps = "";
            if ( PeerTestSupported ) ssucaps += "B";
            if ( IntroductionSupported ) ssucaps += "C";

            addr.Options["caps"] = ssucaps;
            addr.Options["key"] = FreenetBase64.Encode( RouterContext.Inst.IntroKey );
            foreach ( var intro in IntroducersInfo )
            {
                addr.Options[intro.Key] = intro.Value;
            }

            RouterContext.Inst.UpdateAddress( this, addr );
        }
    }
}
