using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2P.I2CP.Messages
{
    public abstract class I2CPMessage
    {
        public enum ProtocolMessageType : byte
        {
            CreateSession = 1,
            ReconfigSession = 2,
            DestroySession = 3,
            CreateLS = 4,
            SendMessage = 5,
            RecvMessageBegin = 6,
            RecvMessageEnd = 7,
            GetBWLimits = 8,
            SessionStatus = 20,
            RequestLS = 21,
            MessageStatus = 22,
            BWLimits = 23,
            ReportAbuse = 29,
            Disconnect = 30,
            MessagePayload = 31,
            GetDate = 32,
            SetDate = 33,
            DestLookup = 34,
            DestLookupRely = 35,
            SendMessageExpires = 36,
            RequestVarLS = 37,
            HostLookup = 38,
            HostLookupReply = 39,
        }

        public ProtocolMessageType MessageType;

        public I2CPMessage( ProtocolMessageType msgtype )
        {
            MessageType = msgtype;
        }

        public abstract void Write( BufRefStream dest );

        public void WriteMessage( BufRefStream dest, params I2PType[] fields )
        {
            var buf = new BufRefStream();
            foreach ( var field in fields ) field.Write( buf );

            dest.Write( BufUtils.Flip32B( (uint)buf.Length ) );
            dest.Write( (byte)MessageType );
            dest.Write( buf );
        }

        public byte[] ToByteArray()
        {
            var buf = new BufRefStream();
            Write( buf );
            return buf.ToArray();
        }
        /*
        public void WriteMessage( BufStream dest )
        {
            var buf = new BufStream();

            Write( buf );

            dest.AddRange( BufUtils.Flip32B( (uint)buf.Count ) );
            dest.Add( (byte)MessageType );
            dest.AddRange( buf );
        }

        public void WriteMessage( Stream dest )
        {
            var buf = new BufStream();
            WriteMessage( buf );
            var ar = buf.ToArray();
            dest.Write( ar, 0, ar.Length );

            DebugUtils.Log( "Send: " + buf.Count );
        }*/

        public static I2CPMessage GetMessage( BufRefLen data )
        {
            switch ( (ProtocolMessageType)data[4] )
            {
                case ProtocolMessageType.CreateSession:
                    break;

                case ProtocolMessageType.ReconfigSession:
                    break;

                case ProtocolMessageType.DestroySession:
                    break;

                case ProtocolMessageType.CreateLS:
                    break;

                case ProtocolMessageType.SendMessage:
                    break;

                case ProtocolMessageType.RecvMessageBegin:
                    break;

                case ProtocolMessageType.RecvMessageEnd:
                    break;

                case ProtocolMessageType.GetBWLimits:
                    break;

                case ProtocolMessageType.SessionStatus:
                    break;

                case ProtocolMessageType.RequestLS:
                    break;

                case ProtocolMessageType.MessageStatus:
                    break;

                case ProtocolMessageType.BWLimits:
                    break;

                case ProtocolMessageType.ReportAbuse:
                    break;

                case ProtocolMessageType.Disconnect:
                    break;

                case ProtocolMessageType.MessagePayload:
                    break;

                case ProtocolMessageType.GetDate:
                    return new GetDateMessage( data );

                case ProtocolMessageType.SetDate:
                    break;

                case ProtocolMessageType.DestLookup:
                    break;

                case ProtocolMessageType.DestLookupRely:
                    break;

                case ProtocolMessageType.SendMessageExpires:
                    break;

                case ProtocolMessageType.RequestVarLS:
                    break;

                case ProtocolMessageType.HostLookup:
                    break;

                case ProtocolMessageType.HostLookupReply:
                    break;

                default:
                    throw new ArgumentException( "I2CP message of type " + data[4].ToString() + " is unknown" );
            }

            throw new NotImplementedException();
        }
    }
}
