using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class TunnelDataFragmentCreation
    {
        protected TunnelDataMessage TDInstance;
        protected TunnelMessage SourceMessage;
        protected BufLen SourceMessageData;
        protected bool Fragmented;

        public TunnelDataFragmentCreation( TunnelDataMessage td, TunnelMessage srcmsg, BufLen tmdata, bool fragmented )
        {
            TDInstance = td;
            SourceMessage = srcmsg;
            SourceMessageData = tmdata;
            Fragmented = fragmented;
        }

        public virtual void Append( BufRef writer )
        {
            switch ( SourceMessage.Delivery )
            {
                case TunnelMessage.DeliveryTypes.Local:
                    writer.Write8( (byte)( (byte)TunnelMessage.DeliveryTypes.Local | ( Fragmented ? 0x08 : 0 ) ) );
                    if ( Fragmented ) writer.Write32( SourceMessage.Message.MessageId );
                    writer.WriteFlip16( (ushort)SourceMessageData.Length );
                    writer.Write( SourceMessageData );
                    break;

                case TunnelMessage.DeliveryTypes.Router:
                    writer.Write8( (byte)( (byte)TunnelMessage.DeliveryTypes.Router | ( Fragmented ? 0x08 : 0 ) ) );
                    writer.Write( ((TunnelMessageRouter)SourceMessage).Destination.Hash );
                    if ( Fragmented ) writer.Write32( SourceMessage.Message.MessageId );
                    writer.WriteFlip16( (ushort)SourceMessageData.Length );
                    writer.Write( SourceMessageData );
                    break;

                case TunnelMessage.DeliveryTypes.Tunnel:
                    writer.Write8( (byte)( (byte)TunnelMessage.DeliveryTypes.Tunnel | ( Fragmented ? 0x08 : 0 ) ) );
                    writer.Write32( ( (TunnelMessageTunnel)SourceMessage ).Tunnel );
                    writer.Write( ( (TunnelMessageTunnel)SourceMessage ).Destination.Hash );
                    if ( Fragmented ) writer.Write32( SourceMessage.Message.MessageId );
                    writer.WriteFlip16( (ushort)SourceMessageData.Length );
                    writer.Write( SourceMessageData );
                    break;

                default:
                    throw new NotImplementedException();
            }
        }
    }

    public class TunnelDataFragmentFollowOn: TunnelDataFragmentCreation
    {
        readonly int FragmentNumber;
        readonly bool LastFragment;
        public TunnelDataFragmentFollowOn( TunnelDataMessage td, TunnelMessage srcmsg, BufLen tmdata, int fragnr, bool lastfrag )
            : base( td, srcmsg, tmdata, true )
        {
            FragmentNumber = fragnr;
            LastFragment = lastfrag;
        }

        public override void Append( BufRef writer )
        {
            writer.Write8( (byte)( 0x80 | ( FragmentNumber << 1 ) | ( LastFragment ? 0x01 : 0x00 ) ) );
            writer.Write32( SourceMessage.Message.MessageId );
            writer.WriteFlip16( (ushort)SourceMessageData.Length );
            writer.Write( SourceMessageData );
        }
    }
}
