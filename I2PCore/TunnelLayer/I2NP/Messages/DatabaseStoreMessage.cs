#define USE_BC_GZIP

using System;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.IO;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class DatabaseStoreMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DatabaseStore; } }

        public enum MessageContent: byte 
        { 
            RouterInfo          = 0b000,
            LeaseSet            = 0b001,
            LeaseSet2           = 0b011, 
            EncryptedLeaseSet   = 0b101,
            MetaLeaseSet        = 0b111,
        }

        public DatabaseStoreMessage( 
            I2PRouterInfo info, 
            uint replytoken, 
            I2PIdentHash replygw, 
            I2PTunnelId replytunnelid )
        {
            BufLen msb;

#if USE_BC_GZIP
            msb = LZUtils.BCGZipCompressNew( new BufLen( info.ToByteArray() ) );
#else

            using ( var ms = new MemoryStream() )
            {
                var buf = info.ToByteArray();

                using ( var gzs = new GZipStream( ms, CompressionMode.Compress ) )
                {
                    gzs.Write( buf, 0, buf.Length );
                    gzs.Flush();
                }

                msb = new BufLen( ms.ToArray() );
            }
#endif

            var len = 32 + 1 + 4 + 2 + msb.Length + ( replytoken != 0 ? 4 + 32 : 0 );
            AllocateBuffer( len );
            var writer = new BufRefLen( Payload );

            writer.Write( info.Identity.IdentHash.Hash );
            writer.Write8( (byte)MessageContent.RouterInfo );
            writer.Write( BitConverter.GetBytes( replytoken ) );
            if ( replytoken != 0 )
            {
                writer.Write( BitConverter.GetBytes( replytunnelid ) );
                if ( replygw == null || replygw.Hash.Length != 32 )
                {
                    throw new FormatException( "ReplyGateway has to be 32 bytes long!" );
                }
                writer.Write( replygw.Hash );
            }

            writer.WriteFlip16( (ushort)msb.Length );
            writer.Write( msb );
        }

        public DatabaseStoreMessage( I2PRouterInfo info ): this( info, 0, null, 0 )
        {
        }

        public DatabaseStoreMessage( 
                ILeaseSet leaseset, 
                uint replytoken, 
                I2PIdentHash replygw, 
                I2PTunnelId replytunnelid )
        {
            var ls = leaseset.ToByteArray();

            AllocateBuffer( 32 + 5 + ( replytoken != 0 ? 4 + 32: 0 ) + ls.Length );
            var writer = new BufRefLen( Payload );

            writer.Write( leaseset.Destination.IdentHash.Hash );
            writer.Write8( (byte)leaseset.MessageType );
            writer.Write32( replytoken );
            if ( replytoken != 0 )
            {
                writer.Write32( replytunnelid );
                if ( replygw == null || replygw.Hash.Length != 32 )
                {
                    throw new FormatException( "ReplyGateway has to be 32 bytes long!" );
                }
                writer.Write( replygw.Hash );
            }

            writer.Write( ls );
            UpdateCachedFields( (BufRefLen)Payload );
        }

        public DatabaseStoreMessage( ILeaseSet leaseset ): this( leaseset, 0, null, 0 )
        {
        }

        I2PIdentHash CachedRouterId;
        MessageContent CachedContentType;
        uint CachedReplyToken;
        uint CachedReplyTunnelId;
        I2PIdentHash CachedReplyGateway;
        I2PRouterInfo CachedRouterInfo;
        ILeaseSet CachedLeaseSet;

        public I2PIdentHash Key 
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedRouterId;
            }
        }

        public MessageContent Content
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedContentType;
            }
        }

        public uint ReplyToken
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyToken;
            }
        }

        public uint ReplyTunnelId
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyTunnelId;
            }
        }

        public I2PIdentHash ReplyGateway
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyGateway;
            }
        }

        public I2PRouterInfo RouterInfo
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedRouterInfo;
            }
        }

        public ILeaseSet LeaseSet
        { 
            get 
            { 
                if ( CachedRouterId == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedLeaseSet;
            }
        }

        public DatabaseStoreMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            UpdateCachedFields( reader );
            SetBuffer( start, reader );
        }

        void UpdateCachedFields( BufRef reader )
        {
            CachedRouterId = new I2PIdentHash( reader );
            CachedContentType = (MessageContent)reader.Read8();
            CachedReplyToken = reader.Read32();
            if ( CachedReplyToken != 0 )
            {
                CachedReplyTunnelId = reader.Read32();
                CachedReplyGateway = new I2PIdentHash( reader );
            }

            //Logging.LogDebug( $"DatabaseStoreMessage: {CachedContentType}, {CachedRouterId?.Id32Short}, {CachedReplyToken}" );

            switch ( CachedContentType )
            {
                case MessageContent.RouterInfo:
                    var length = reader.ReadFlip16();

#if USE_BC_GZIP
                    CachedRouterInfo = new I2PRouterInfo(
                        new BufRefLen( LZUtils.BCGZipDecompressNew( new BufLen( reader, 0, length ) ) ), true );
#else
                    using ( var ms = new MemoryStream() )
                    {
                        ms.Write( reader.BaseArray, reader.BaseArrayOffset, length );
                        ms.Position = 0;

                        using ( var gzs = new GZipStream( ms, CompressionMode.Decompress ) )
                        {
                            var gzdata = StreamUtils.Read( gzs );
                            CachedRouterInfo = new I2PRouterInfo( new BufRefLen( gzdata ), true );
                        }
                    }
#endif

                    reader.Seek( length );
                    break;

                case MessageContent.LeaseSet:
                    CachedLeaseSet = new I2PLeaseSet( reader );
                    break;

                case MessageContent.LeaseSet2:
                    CachedLeaseSet = new I2PLeaseSet2( reader );
                    break;

                    /*
                case MessageContent.EncryptedLeaseSet:
                    break;

                case MessageContent.MetaLeaseSet:
                    break;
                    */
                default:
                    /* TODO: Fix NetDb.Inst.Statistics.DestinationInformationFaulty( CachedRouterId ); */
                    throw new InvalidDataException( $"DatabaseStoreMessage: {CachedContentType} not supported for destination {CachedRouterId.Id32Short}" );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( $"DatabaseStore {Content}, key {Key.Id32Short}" );
            result.AppendLine( $"Reply token {ReplyToken}, tunnel {ReplyTunnelId}, GW {ReplyGateway}" );

            switch ( CachedContentType )
            {
                case MessageContent.RouterInfo:
                    result.AppendLine( $"{RouterInfo}" );
                    break;

                case MessageContent.LeaseSet:
                    result.AppendLine( $"{LeaseSet}" );
                    break;

                case MessageContent.LeaseSet2:
                    result.AppendLine( $"LeaseSet2" );
                    break;

                case MessageContent.EncryptedLeaseSet:
                    result.AppendLine( $"EncryptedLeaseSet" );
                    break;

                case MessageContent.MetaLeaseSet:
                    result.AppendLine( $"MetaLeaseSet" );
                    break;

                default:
                    result.AppendLine( $"Unknown content type" );
                    break;
            }

            return result.ToString();
        }
    }
}
