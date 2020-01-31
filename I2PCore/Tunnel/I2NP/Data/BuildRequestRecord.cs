using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class BuildRequestRecord: I2PType
    {
        public const int Length = 222;

        public BufLen Data;

        public BuildRequestRecord()
        {
            Data = new BufLen( new byte[Length] );
        }

        public BuildRequestRecord( BufRef buf )
        {
            Data = buf.ReadBufLen( Length );
        }

        public I2PTunnelId ReceiveTunnel { get { return new I2PTunnelId( Data.Peek32( 0 ) ); } set { Data.Poke32( value, 0 ); } }
        public I2PIdentHash OurIdent { get { return new I2PIdentHash( new BufRefLen( Data, 4, 32 ) ); } set { Data.Poke( value.Hash, 4, 32 ); } }
        public I2PTunnelId NextTunnel { get { return new I2PTunnelId( Data.Peek32( 36 ) ); } set { Data.Poke32( value, 36 ); } }
        public I2PIdentHash NextIdent { get { return new I2PIdentHash( new BufRefLen( Data, 40, 32 ) ); } set { Data.Poke( value.Hash, 40 ); } }
        public BufLen LayerKey { get { return new BufLen( Data, 72, 32 ); } }
        public BufLen IVKey { get { return new BufLen( Data, 104, 32 ); } }
        public I2PSessionKey ReplyKey { get { return new I2PSessionKey( new BufRefLen( Data, 136, 32 ) ); } set { Data.Poke( value.Key, 136 ); } }
        public BufLen ReplyKeyBuf { get { return new BufLen( Data, 136, 32 ); } }
        public BufLen ReplyIV { get { return new BufLen( Data, 168, 16 ); } }
        public byte Flag { get { return Data.Peek8( 184 ); } set { Data.Poke8( value, 184 ); } }

        public uint RequestTimeVal { get { return Data.PeekFlip32( 185 ); } set { Data.PokeFlip32( value, 185 ); } }
        public DateTime RequestTime 
        { 
            get 
            { 
                return I2PDate.RefDate.AddHours( RequestTimeVal ); 
            } 
            set 
            { 
                RequestTimeVal = (uint)Math.Truncate( ( value - I2PDate.RefDate ).TotalHours );
            } 
        }

        public uint SendMessageId { get { return Data.Peek32( 189 ); } set { Data.Poke32( value, 189 ); } }
        public BufLen Padding { get { return new BufLen( Data, 193, 29 ); } }

        /// <summary>
        /// Is inbound gateway.
        /// </summary>
        public bool FromAnyone
        {
            get
            {
                return ( Flag & 0x80 ) != 0;
            }
            set
            {
                if ( ToAnyone && value ) throw new InvalidOperationException( "Both To and From anyone cannot be set at the same time!" );
                Flag = (byte)( ( Flag & 0x7F ) | ( value ? 0x80 : 0 ) );
            }
        }

        /// <summary>
        /// Is outbound endpoint.
        /// </summary>
        public bool ToAnyone
        {
            get
            {
                return ( Flag & 0x40 ) != 0;
            }
            set
            {
                if ( FromAnyone && value ) throw new InvalidOperationException( "Both To and From anyone cannot be set at the same time!" );
                Flag = (byte)( ( Flag & 0xBF ) | ( value ? 0x40 : 0 ) );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "BuildRequestRecord" );
            result.AppendLine( "ReceiveTunnel : " + ReceiveTunnel.ToString() );
            result.AppendLine( "OurIdent      : " + OurIdent.ToString() );
            result.AppendLine( "NextTunnel    : " + NextTunnel.ToString() );
            result.AppendLine( "NextIdent     : " + NextIdent.ToString() );
            result.AppendLine( "Flag          : 0x" + Flag.ToString( "X2" ) );
            result.AppendLine( "ToAnyone      : " + ToAnyone.ToString() );
            result.AppendLine( "FromAnyone    : " + FromAnyone.ToString() );
            result.AppendLine( "RequestTime   : " + RequestTime.ToString() );
            result.AppendLine( "SendMessageId : " + SendMessageId.ToString() );

            return result.ToString();
        }

        /// <summary>
        /// High probability to match with similar route.
        /// </summary>
        /// <returns></returns>
        public uint GetReducedHash()
        {
            if ( FromAnyone )
            {
                var result = ReceiveTunnel.GetHashCode();
                result ^= NextIdent.GetHashCode();
                return (uint)result;
            }
            else if ( ToAnyone )
            {
                var result = ReceiveTunnel.GetHashCode();
                result ^= NextIdent.GetHashCode();
                return (uint)result;
            }

            {
                var result = ReceiveTunnel.GetHashCode();
                result ^= NextIdent.GetHashCode();
                result ^= NextTunnel.GetHashCode();
                return (uint)result;
            }
        }

        public uint GetHash()
        {
            return (uint)Data.GetHashCode();
        }

        void I2PType.Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }
    }
}
