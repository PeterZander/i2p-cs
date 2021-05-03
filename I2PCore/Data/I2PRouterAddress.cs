using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using I2PCore.Utils;
using System.Net.Sockets;

namespace I2PCore.Data
{
    public class I2PRouterAddress : I2PType
    {
        public byte Cost;
        public I2PDate Expiration; // Must be all 0!
        public I2PString TransportStyle;
        public I2PMapping Options;

        public IPAddress Host 
        { 
            get 
            {
                if ( !Options.TryGet( "host", out var hoststr ) ) return null;

                var host = hoststr.ToString();

                var fam = IPTestHostName( host );
                if ( fam == AddressFamily.InterNetwork || fam == AddressFamily.InterNetworkV6 )
                    return IPAddress.Parse( host );

                var al = Dns.GetHostEntry( host ).AddressList.
                    Where( a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork );
                if ( al.Any() )
                {
                    return al.Random();
                }
                return null;
            } 
        }

        public int Port { get { return Options.TryGet( "port", out var port ) ? int.Parse( port.ToString() ) : -1; } }

        public bool HaveHostAndPort { get { return Options.Contains( "host" ) && Options.Contains( "port" ); } }

        public I2PRouterAddress( IPAddress addr, int port, byte cost, string transportstyle )
        {
            Cost = cost;
            Expiration = I2PDate.Zero;
            TransportStyle = new I2PString( transportstyle );
            Options = new I2PMapping();

            Options["host"] = addr.ToString();
            Options["port"] = port.ToString();
        }

        public I2PRouterAddress( BufRef buf )
        {
            Cost = buf.Read8();
            Expiration = new I2PDate( buf );
            TransportStyle = new I2PString( buf );
            Options = new I2PMapping( buf );
        }

        public void Write( BufRefStream dest )
        {
            // Routers MUST set this (expire) field to all zeros. As of release 0.9.12, 
            // a non-zero expiration field is again recognized, however we must 
            // wait several releases to use this field, until the vast majority 
            // of the network recognizes it.
            // TODO: Hmmm?

            dest.Write( Cost );
            Expiration.Write( dest );
            TransportStyle.Write( dest );
            Options.Write( dest );
        }

        public static AddressFamily IPTestHostName( string host )
        {
            if ( IPAddress.TryParse( host, out var address ) )
            {
                return address.AddressFamily;
            }

            return AddressFamily.Unknown;
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PRouterAddress" );

            result.AppendLine( "Cost         : " + Cost.ToString() );
            result.AppendLine( "Expiration 0 : " + Expiration.ToString() );
            result.AppendLine( "Transport    : " + TransportStyle );
            result.AppendLine( "Options      : " + Options.ToString() );

            return result.ToString();
        }
        
    }
}
