using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP
{
    public static class I2NPUtil
    {
        public static I2NPMessage GetMessage( 
                I2NPMessage.MessageTypes messagetype, 
                BufRef reader, 
                uint? msgid = null )
        {
            I2NPMessage result = null;

            try
            {
                switch ( messagetype )
                {
                    case I2NPMessage.MessageTypes.Garlic:
                        result = new GarlicMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.Data:
                        result = new DataMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.DatabaseSearchReply:
                        result = new DatabaseSearchReplyMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.DatabaseStore:
                        result = new DatabaseStoreMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.DeliveryStatus:
                        result = new DeliveryStatusMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.TunnelData:
                        result = new TunnelDataMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.TunnelGateway:
                        result = new TunnelGatewayMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.DatabaseLookup:
                        result = new DatabaseLookupMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.VariableTunnelBuild:
                        result = new VariableTunnelBuildMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.TunnelBuild:
                        result = new TunnelBuildMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.TunnelBuildReply:
                        result = new TunnelBuildReplyMessage( reader );
                        break;

                    case I2NPMessage.MessageTypes.VariableTunnelBuildReply:
                        result = new VariableTunnelBuildReplyMessage( reader );
                        break;

                    default:
                        Logging.LogDebug( $"GetMessage: '{messagetype}' is not a known message type!" );
                        throw new NotImplementedException();
                }
            }
            catch ( Exception ex )
            {
                Logging.Log( "GetMessage", ex );
                throw;
            }

            if ( result != null && msgid.HasValue )
            {
                result.MessageId = msgid.Value;
            }

            return result;
        }
    }
}
