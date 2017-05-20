using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class TunnelDataFragment
    {
        BufRef Data;

        public byte Flag { get { return Data[0]; } set { Data[0] = value; } }

        public bool InitialFragment { get { return ( Flag & 0x80 ) == 0; } set { Flag = (byte)( ( Flag & 0x7F ) | ( value ? 0 : 0x80 ) ); } }
        public bool FollowOnFragment { get { return !InitialFragment; } set { InitialFragment = !value; } }
        
        public bool Fragmented 
        { 
            get 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                return ( Flag & 0x08 ) != 0; 
            } 
            set 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                Flag = (byte)( ( Flag & 0xF7 ) | ( value ? 0x08 : 0 ) ); 
            } 
        }

        public TunnelMessage.DeliveryTypes Delivery 
        { 
            get 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                return (TunnelMessage.DeliveryTypes)( Flag & (byte)TunnelMessage.DeliveryTypes.Unused ); 
            } 
            set 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                Flag = (byte)( ( Flag & ~(byte)TunnelMessage.DeliveryTypes.Unused ) | (byte)value ); 
            } 
        }

        public bool ExtendedOptions 
        { 
            get 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                return ( Flag & 0x04 ) != 0; 
            } 
            set 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                Flag = (byte)( ( Flag & 0xFB ) | ( value ? 0x04 : 0 ) ); 
            } 
        }

        public bool Delayed 
        { 
            get 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                return ( Flag & 0x10 ) != 0; 
            } 
            set 
            {
                if ( FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is Follow On Fragment" );
                Flag = (byte)( ( Flag & 0xEF ) | ( value ? 0x10 : 0 ) ); 
            } 
        }

        BufLen TunnelRef;
        public I2PTunnelId Tunnel { get { return new I2PTunnelId( new BufRef( TunnelRef ) ); } set { value.Write( new BufRef( TunnelRef ) ); } }

        BufLen ToHashRef;
        public BufLen ToHash { get { return new BufLen( ToHashRef ); } }

        BufRef DelayRef;
        public byte Delay { get { return DelayRef[0]; } set { DelayRef[0] = value; } }

        // Follow on properties
        public byte FragmentNumber
        {
            get 
            {
                if ( !FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is not Follow On Fragment" );
                return (byte)( ( Flag & 0x7E ) >> 1 ); 
            }
            set 
            {
                if ( !FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is not Follow On Fragment" );
                Flag = (byte)( ( Flag & 0x7E ) | ( value << 1 ) ); 
            }
        }

        public bool LastFragment 
        { 
            get 
            {
                if ( !FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is not Follow On Fragment" );
                return ( Flag & 0x01 ) != 0; 
            } 
            set 
            {
                if ( !FollowOnFragment ) throw new ArgumentException( "TunnelDataFragment is not Follow On Fragment" );
                Flag = (byte)( ( Flag & 0xFE ) | ( value ? 0x01 : 0 ) ); 
            } 
        }

        // Shared properties 

        BufRef MessageIdRef;
        public uint MessageId { get { return MessageIdRef.Peek32( 0 ); } set { MessageIdRef.Poke32( value, 0 ); } }

        BufLen PayloadRef;
        public BufRefLen Payload { get { return new BufRefLen( PayloadRef ); } }

        public TunnelDataFragment( BufRef buf )
        {
            Data = new BufRef( buf );

            var reader = buf;
            reader.Seek( 1 ); // Flag

            if ( InitialFragment )
            {
                switch ( Delivery )
                {
                    case TunnelMessage.DeliveryTypes.Local:
                        if ( Delayed )
                        {
                            DelayRef = reader.ReadBufRef( 1 );
                        }
                        if ( Fragmented )
                        {
                            MessageIdRef = reader.ReadBufRef( 4 );
                        }
                        if ( ExtendedOptions )
                        {
                            var len = reader.Read8();
                            reader.Seek( len );
                        }
                        break;

                    case TunnelMessage.DeliveryTypes.Router:
                        ToHashRef = reader.ReadBufLen( 32 );

                        if ( Delayed )
                        {
                            DelayRef = reader.ReadBufRef( 1 );
                        }
                        if ( Fragmented )
                        {
                            MessageIdRef = reader.ReadBufRef( 4 );
                        }
                        if ( ExtendedOptions )
                        {
                            var len = reader.Read8();
                            reader.Seek( len );
                        }
                        break;

                    case TunnelMessage.DeliveryTypes.Tunnel:
                        TunnelRef = reader.ReadBufLen( 4 );
                        ToHashRef = reader.ReadBufLen( 32 );

                        if ( Delayed )
                        {
                            DelayRef = reader.ReadBufRef( 1 );
                        }
                        if ( Fragmented )
                        {
                            MessageIdRef = reader.ReadBufRef( 4 );
                        }
                        if ( ExtendedOptions )
                        {
                            var len = reader.Read8();
                            reader.Seek( len );
                        }
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                // Follow on
                MessageIdRef = reader.ReadBufRef( 4 );
            }

            var payloadlen = reader.ReadFlip16();
            PayloadRef = reader.ReadBufLen( payloadlen );
        }
    }
}
