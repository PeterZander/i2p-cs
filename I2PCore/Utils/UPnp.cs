#define LOG_ALL_UPNP

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using I2PCore.Utils;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml;
using System.IO;
using I2PCore.Router;

namespace I2PCore.Utils
{
    // Brute force Upnp stuff.

    public class UPnp
    {
        EndPoint MSEp;
        EndPoint MLEp;
        Socket MulticastSocket;

        const int LeaseMapDurationSeconds = 60 * 60 * 2;

        Dictionary<string, HttpResponse> WANIPConnections = new Dictionary<string, HttpResponse>();
        Dictionary<string, ControlInfo> WANIPConnectionsCtlInfo = new Dictionary<string, ControlInfo>();

        internal class ControlInfo
        {
            internal string Url;
            internal string HostName;
            internal string Host;
            internal int Port;
        }

        const string UpnpActionHtmlHeader = "POST ¤ctlurl¤ HTTP/1.0\r\n" +
            "HOST: ¤hostname¤\r\n" +
            "CONTENT-LENGTH: ¤cl¤\r\n" +
            "CONTENT-TYPE: text/xml; charset=\"utf-8\"\r\n" +
            "USER-AGENT: Windows/7.0 UPnP/1.1 I2Ps/1.0\r\n" +
            "SOAPACTION: \"urn:schemas-upnp-org:service:¤servicetype¤#¤actionname¤\"\r\n\r\n";

        const string UpnpActionXml = "<?xml version=\"1.0\"?>\r\n" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
            "<s:Body><u:¤actionname¤ xmlns:u=\"urn:schemas-upnp-org:service:¤servicetype¤\"> ¤arguments¤ </u:¤actionname¤>\r\n" +
            "</s:Body></s:Envelope>";

        public UPnp()
        {
            var ip = IPAddress.Parse( "239.255.255.250" );

            IPAddress local = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.Where( ia => ( ia.AddressFamily == AddressFamily.InterNetwork ) ).First();
            MSEp = new IPEndPoint( ip, 1900 );
            MLEp = new IPEndPoint( IPAddress.Any, 1900 );

            MulticastSocket = new Socket( AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp );
            MulticastSocket.Bind( MLEp );
            MulticastSocket.SetSocketOption( SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption( ip ) );
            MulticastSocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true );
            MulticastSocket.SetSocketOption( SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 3 );
            MulticastSocket.SetSocketOption( SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false ); 

            Worker = new Thread( () => Run() );
            Worker.Name = "UPnp";
            Worker.IsBackground = true;
            Worker.Start();
        }

        protected Thread Worker;

        bool Terminated = false;
        PeriodicAction DiscoverAction = new PeriodicAction( TickSpan.Minutes( 15 ) );
        PeriodicAction ExternalPortMappingAction = new PeriodicAction( TickSpan.Seconds( 0 ) );
        PeriodicAction GetExternalAddressAction = new PeriodicAction( TickSpan.Seconds( 0 ) );
        private void Run()
        {
            var buf = new byte[65536];
            var buf2 = new byte[65536];
            try
            {
                MulticastSocket.BeginReceiveFrom( buf, 0, buf.Length, SocketFlags.None, ref MLEp, new AsyncCallback( ReceiveMulticast ), buf );

                DiscoverDevices();
                Thread.Sleep( 1000 );
                while ( !Terminated )
                {
                    try
                    {
                        DiscoverAction.Do( DiscoverDevices );

                        GetExternalAddressAction.Do( delegate
                        {
                            if ( GetExternalAddressAction.Frequency.ToSeconds < 1.0 )
                            {
                                GetExternalAddressAction.Frequency = TickSpan.Seconds( 60 * 30 );
                                GetExternalAddressAction.Start();
                            }

                            if ( WANIPConnectionsCtlInfo.Count > 0 )
                            {
                                ControlInfo ci;
                                lock ( WANIPConnectionsCtlInfo )
                                {
                                    ci = WANIPConnectionsCtlInfo.Values.First();
                                }
                                RequestExternalIpAddress( ci );
                            }
                        } );

                        ExternalPortMappingAction.Do( delegate
                        {
                            if ( ExternalPortMappingAction.Frequency.ToSeconds < 1.0 )
                            {
                                ExternalPortMappingAction.Frequency = TickSpan.Seconds( LeaseMapDurationSeconds - 200 );
                                ExternalPortMappingAction.Start();
                            }

                            if ( WANIPConnectionsCtlInfo.Count > 0 )
                            {
                                UpdatePortMapping( "TCP" );
                                UpdatePortMapping( "UDP" );
                            }
                        } );

                        Thread.Sleep( 1000 );
                    }
                    catch ( ThreadAbortException ex )
                    {
                        Logging.Log( ex );
                    }
                    catch ( Exception ex )
                    {
                        Logging.Log( ex );
                    }
                }
            }
            finally
            {
                Terminated = true;
            }
        }

        int MappedExternalPort = 6359;

        private void UpdatePortMapping( string protocol )
        {
            ControlInfo ci;
            lock ( WANIPConnectionsCtlInfo )
            {
                ci = WANIPConnectionsCtlInfo.Values.First();
            }
            var current = GetSpecificPortMappingEntry( ci, protocol, MappedExternalPort );

            var addr = Dns.GetHostEntry( Dns.GetHostName() ).AddressList.First( a => a.AddressFamily == AddressFamily.InterNetwork );

            var duration = -1;
            var client = "";
            if ( current != null )
            {
                if ( current.ContainsKey( "NewLeaseDuration" ) )
                {
                    duration = int.Parse( current["NewLeaseDuration"] );
                }

                if ( current.ContainsKey( "NewInternalClient" ) )
                {
                    client = current["NewInternalClient"];
                }

                if ( duration == 0 && client == addr.ToString() )
                {
                    RouterContext.Inst.UpnpNATPortMapAdded( addr, protocol, MappedExternalPort );

                    Logging.LogInformation( "Upnp: " + protocol + " port " + MappedExternalPort.ToString() + " already forwarded." );
                    return; // We have mapped the port, and the gateway does not support decaying leases.
                }
            }

            var port = MappedExternalPort;
            while ( !AddPortMapping( ci, protocol, addr.ToString(), port, port ) && ++port < 7000 ) ;

            if ( port >= 7000 )
            {
                Logging.LogInformation( "Upnp: Failed to map an external " + protocol + " port." );
                return;
            }

            MappedExternalPort = port;

            Logging.LogInformation( "Upnp: " + protocol + " Port " + MappedExternalPort.ToString() + " mapped to same local port." );

            RouterContext.Inst.UpnpNATPortMapAdded( addr, protocol, MappedExternalPort );
        }

        private void ReceiveMulticast( IAsyncResult ar )
        {
            var buf = (byte[])ar.AsyncState;
            try
            {
                var len = MulticastSocket.EndReceiveFrom( ar, ref MLEp );

                var txt = Encoding.UTF8.GetString( buf, 0, len );
                var resp = ParseResponse( txt );

#if LOG_ALL_UPNP
                Logging.Log( "UPnp multicast data received: " + MLEp.ToString() + ":" + txt );
#endif

                if ( resp != null )
                {
                    CaptureWANIPConnection( resp );
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( ex );
            }
            finally
            {
                MulticastSocket.BeginReceiveFrom( buf, 0, buf.Length, SocketFlags.None, ref MLEp, new AsyncCallback( ReceiveMulticast ), buf );
            }
        }

        private void CaptureWANIPConnection( HttpResponse resp )
        {
            if ( 
                ( resp.Headers.ContainsKey( "ST" ) && resp.Headers["ST"].Contains( "WANIPConnection:1" ) ) ||
                ( resp.Headers.ContainsKey( "NT" ) && resp.Headers["NT"].Contains( "WANIPConnection:1" ) )
                )
            {
                if ( resp.Headers.ContainsKey( "USN" ) && resp.Headers.ContainsKey( "LOCATION" ) )
                {
                    var usnline = resp.Headers["USN"];
                    var usnmatch = Regex.Match( usnline, "uuid:([0-9a-fA-F\\-]+)" );
                    if ( usnmatch.Success )
                    {
                        var key = usnmatch.Groups[1].Captures[0].Value;
                        Logging.LogDebug( "Upnp: Found USN: " + key );

                        bool isnew = !( WANIPConnections.ContainsKey( key ) );

                        lock ( WANIPConnections )
                        {
                            WANIPConnections[key] = resp;
                        }

                        if ( isnew )
                        {
                            NewWANIPConnectionFound( key, resp );
                        }
                    }
                }
                else
                {
                    Logging.LogDebug( "Upnp: No LOCATION or USN in response." );
                }
            }
        }

        private void NewWANIPConnectionFound( string id, HttpResponse resp )
        {
            var location = resp.Headers["LOCATION"];

#if LOG_ALL_UPNP
            Logging.Log( "Upnp: NewWANIPConnectionFound: LOCATION: " + location );
#endif
            var xmlreq = HttpWebRequest.Create( location );
            xmlreq.Timeout = 30 * 1000;
            var response = xmlreq.GetResponse();
            var xml = new XmlDocument();

            var sr = new StreamReader( response.GetResponseStream() );
            var st = sr.ReadToEnd();

#if LOG_ALL_UPNP
            Logging.Log( "Upnp: XML: " + st );
#else
            Logging.Log( "Upnp: Got device description XML." );
#endif
            st = StripNamespaces( st );

            xml.LoadXml( st );

            string hostname;

            {
                var urlnode = xml.SelectSingleNode( "//URLBase" ); // Only present in UPnP 1.0, not in 1.1.
                if ( urlnode != null )
                {
                    hostname = urlnode.InnerText;
                }
                else
                {
                    hostname = location; // UPnP 1.1
                }
            }

            hostname = hostname.Replace( "http://", "" );

            if ( hostname.Contains( '/' ) )
            {
                hostname = hostname.Substring( 0, hostname.IndexOf( '/' ) );
            }

            hostname = hostname.Trim();

            var portix = hostname.IndexOf( ":" );
            string host;
            int port;
            if ( portix != -1 )
            {
                host = hostname.Substring( 0, portix ).Trim();
                port = int.Parse( hostname.Substring( portix + 1 ).Trim() );
            }
            else
            {
                host = hostname.Trim();
                port = 80;
            }

            var connnodes = xml.SelectNodes( "//service[serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"][controlURL]" );
            if ( connnodes.Count == 1 )
            {
                var ctlurl = connnodes[0].SelectSingleNode( "controlURL" ).InnerText;
                var ctlinfo = new ControlInfo() { Url = ctlurl, HostName = hostname, Host = host, Port = port };
                lock ( WANIPConnectionsCtlInfo )
                {
                    WANIPConnectionsCtlInfo[id] = ctlinfo;
                }

                RequestExternalIpAddress( ctlinfo );
            }
            else
            {
                Logging.LogDebug( "Upnp: Node count: " + connnodes.Count.ToString() );
            }
        }

        private void RequestExternalIpAddress( ControlInfo ctlinfo )
        {
            var st = UpnpAction( ctlinfo, "GetExternalIPAddress", "" );

            if ( st == null ) return;

            var resp = ParseResponse( st );
            if ( resp.Error / 100 != 2 )
            {
                Logging.LogInformation( "Upnp: Requesting external IP# failed: " + resp.Error.ToString() );
                return;
            }

            var docst = StripNamespaces( resp.Body );

            var xml = new XmlDocument();
            xml.LoadXml( docst );

            var ipnode = xml.SelectSingleNode( "//NewExternalIPAddress" );
            if ( ipnode != null )
            {
                RouterContext.Inst.UpnpReportedAddr( ipnode.InnerText );
                Logging.LogInformation( "Upnp: External IP#: " + ipnode.InnerText );
            }
        }

        private Dictionary<string, string> GetSpecificPortMappingEntry( ControlInfo ctlinfo, string proto, int gatewayport )
        {
            var args = "<NewRemoteHost/><NewExternalPort>" + gatewayport.ToString() + "</NewExternalPort>" +
                "<NewProtocol>" + proto + "</NewProtocol>";
            var st = UpnpAction( ctlinfo, "GetSpecificPortMappingEntry", args );

            /*
            */

            if ( st == null ) return null;

            var resp = ParseResponse( st );
            if ( resp.Error / 100 != 2 )
            {
                Logging.LogInformation( "Upnp: Requesting status on external port mapping failed: HTTP err " + resp.Error.ToString() );
                return null;
            }

            var docst = StripNamespaces( resp.Body );

            var xml = new XmlDocument();
            xml.LoadXml( docst );

            var node = xml.SelectSingleNode( "//GetSpecificPortMappingEntryResponse" );
            var child = node.FirstChild;
            var result = new Dictionary<string, string>();
            while ( child != null )
            {
                result[child.Name] = child.InnerText;
                child = child.NextSibling;
            }

            return result;
        }

        private bool AddPortMapping( ControlInfo ctlinfo, string proto, string internalcli, int gatewayport, int localport )
        {
            var args = "<NewRemoteHost/><NewExternalPort>" + gatewayport.ToString() + "</NewExternalPort>" +
                "<NewProtocol>" + proto + "</NewProtocol>" +
                "<NewInternalPort>" + localport.ToString() + "</NewInternalPort><NewInternalClient>" + internalcli + "</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled><NewPortMappingDescription>I2Ps</NewPortMappingDescription>" +
                "<NewLeaseDuration>" + LeaseMapDurationSeconds.ToString() + "</NewLeaseDuration>";
            var st = UpnpAction( ctlinfo, "AddPortMapping", args );

            /*
            */

            if ( st == null ) return false;

            var resp = ParseResponse( st );
            if ( resp.Error / 100 != 2 )
            {
                Logging.LogInformation( "Upnp: Requesting external port mapping failed: HTTP err " + resp.Error.ToString() );
                return false;
            }

            var docst = StripNamespaces( resp.Body );

            var xml = new XmlDocument();
            xml.LoadXml( docst );

            var ipnode = xml.SelectSingleNode( "//errorCode" );
            if ( ipnode != null )
            {
                Logging.LogInformation( "Upnp: Requesting external port mapping failed: " + docst );
                return false;
            }

            return true;
        }

        private static string StripNamespaces( string st )
        {
            string filter = @"xmlns(:\w+)?=""([^""]+)""|xsi(:\w+)?=""([^""]+)""";
            st = Regex.Replace( st, filter, "" );

            filter = @"<\w+:";
            st = Regex.Replace( st, filter, "<" );

            filter = @"</\w+:";
            st = Regex.Replace( st, filter, "</" );

            filter = @"\w+:(\w+)=";
            st = Regex.Replace( st, filter, "$1=" );
            return st;
        }

        private string UpnpAction( ControlInfo ctlinfo, string action, string arguments )
        {
            var soap = UpnpActionXml.Replace( "¤actionname¤", action );
            soap = soap.Replace( "¤arguments¤", arguments );
            soap = soap.Replace( "¤servicetype¤", "WANIPConnection:1" );

            var st = UpnpActionHtmlHeader.Replace( "¤hostname¤", ctlinfo.HostName );
            st = st.Replace( "¤ctlurl¤", ctlinfo.Url );
            st = st.Replace( "¤servicetype¤", "WANIPConnection:1" );
            st = st.Replace( "¤actionname¤", action );
            st = st.Replace( "¤cl¤", soap.Length.ToString() );

            using ( var tc = new TcpClient() )
            {
                IPAddress addr;

                if ( System.Net.IPAddress.TryParse( ctlinfo.Host, out addr ) )
                {
                    tc.Connect( addr, ctlinfo.Port );
                }
                else
                {
                    tc.Connect( ctlinfo.Host, ctlinfo.Port );
                }

                st = st + soap;
#if LOG_ALL_UPNP
                Logging.Log( "Upnp: Sending : " + st );
#endif

                var buf = Encoding.ASCII.GetBytes( st );

                var stream = tc.GetStream();
                stream.Write( buf );
                stream.Flush();

                Thread.Sleep( 100 );

                var respbuf = new byte[32768];
                var pos = 0;

                string respst = "";
                int contentlength = -1;

                while ( true )
                {
                    var len = stream.Read( respbuf, pos, respbuf.Length - pos );
                    if ( len == 0 ) break;
                    pos += len;

                    respst = Encoding.UTF8.GetString( respbuf, 0, pos );
                    var headerend = respst.IndexOf( "\r\n\r\n" );
                    if ( respbuf.Length - pos <= 0 ) break;
                    if ( headerend == -1 ) continue;

                    if ( contentlength == -1 )
                    {
                        var parse = ParseResponse( respst );
                        if ( parse.Headers.ContainsKey( "CONTENT-LENGTH" ) )
                        {
                            contentlength = int.Parse( parse.Headers["CONTENT-LENGTH"] );
                        }
                    }

                    if ( contentlength != -1 && pos >= headerend + 4 + contentlength ) break;
                };

                
#if LOG_ALL_UPNP
                Logging.Log( "Upnp: Response: " + respst );
#endif
                return respst;
            }
        }

        class HttpResponse
        {
            internal bool Notify;
            internal int Error;
            internal IDictionary<string, string> Headers = new Dictionary<string,string>();
            internal string Body;
        }

        HttpResponse ParseResponse( string response )
        {
            var splitpos = response.IndexOf( "\r\n\r\n" );
            if ( splitpos < 0 ) return null;

            var result = new HttpResponse();

            var header = response.Substring( 0, splitpos );
            result.Body = response.Substring( splitpos + 4 );

            var http = Regex.Match( header, "HTTP/1.[01] ([0-9]+) .*" );
            if ( !http.Success )
            {
                var notify = Regex.Match( header, "NOTIFY \\* HTTP/1.[01]" );
                if ( !notify.Success ) return null;
                result.Notify = true;
            }

            if ( !result.Notify )
            {
                result.Error = int.Parse( http.Groups[1].Captures[0].Value );
            }

            var hrows = header.Replace( "\r", "" ).Split( '\n' );
            if ( hrows.Length <= 1 ) return result;

            foreach ( var line in hrows.Skip( 1 ) )
            {
                var keysplit = line.IndexOf( ':' );
                if ( keysplit < 0 ) continue;
                result.Headers[line.Substring( 0, keysplit ).ToUpper()] = line.Substring( keysplit + 1 );
            }

            return result;
        }

        private void DiscoverDevices()
        {
            Logging.Log( "UPnp polling for devices." );

            string ss = "M-SEARCH * HTTP/1.1\r\n" +
                "HOST:239.255.255.250:1900\r\n" +
                "ST:urn:schemas-upnp-org:service:WANIPConnection:1\r\n" +
                "MAN:\"ssdp:discover\"\r\n" +
                "MX:3\r\n\r\n";
             
            var buf = Encoding.ASCII.GetBytes( ss );
            MulticastSocket.SendTo( buf, SocketFlags.None, MSEp );
        }

    }
}
