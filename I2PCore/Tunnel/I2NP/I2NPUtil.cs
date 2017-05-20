using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP
{
    public static class I2NPUtil
    {
        public static I2NPMessage GetMessage( I2NPHeader header, BufRef reader )
        {
            try
            {
                switch ( header.MessageType )
                {
                    case I2NPMessage.MessageTypes.Garlic:
                        return new GarlicMessage( header, reader );

                    case I2NPMessage.MessageTypes.Data:
                        return new DataMessage( header, reader );

                    case I2NPMessage.MessageTypes.DatabaseSearchReply:
                        return new DatabaseSearchReplyMessage( reader );

                    case I2NPMessage.MessageTypes.DatabaseStore:
                        return new DatabaseStoreMessage( header, reader );

                    case I2NPMessage.MessageTypes.DeliveryStatus:
                        return new DeliveryStatusMessage( header, reader );

                    case I2NPMessage.MessageTypes.TunnelData:
                        return new TunnelDataMessage( header, reader );

                    case I2NPMessage.MessageTypes.TunnelGateway:
                        return new TunnelGatewayMessage( reader );

                    case I2NPMessage.MessageTypes.DatabaseLookup:
                        return new DatabaseLookupMessage( header, reader );

                    case I2NPMessage.MessageTypes.VariableTunnelBuild:
                        return new VariableTunnelBuildMessage( reader );

                    case I2NPMessage.MessageTypes.TunnelBuild:
                        return new TunnelBuildMessage( reader );

                    case I2NPMessage.MessageTypes.TunnelBuildReply:
                        return new TunnelBuildReplyMessage( reader );

                    case I2NPMessage.MessageTypes.VariableTunnelBuildReply:
                        return new VariableTunnelBuildReplyMessage( reader );

                    default:
                        DebugUtils.LogDebug( "GetMessage: '" + header.MessageType.ToString() + "' is not a known message type!" );
                        throw new NotImplementedException();
                }
            }
            catch ( Exception ex )
            {
                DebugUtils.Log( "GetMessage", ex );
                throw;
            }
        }
    }
}
