#define USE_BC_GZIP

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.IO;
using System.IO.Compression;
using Org.BouncyCastle.Utilities.Encoders;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class DatabaseStoreMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DatabaseStore; } }

        public enum MessageContent: byte 
        { 
            RouterInfo = 0, 
            LeaseSet = 1, 
            LeaseSet2 = 3, 
            EncryptedLeaseSet = 5, 
            MetaLeaseSet = 7 
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
                I2PLeaseSet leaseset, 
                uint replytoken, 
                I2PIdentHash replygw, 
                I2PTunnelId replytunnelid )
        {
            var ls = leaseset.ToByteArray();

            AllocateBuffer( 32 + 5 + ( replytoken != 0 ? 4 + 32: 0 ) + ls.Length );
            var writer = new BufRefLen( Payload );

            writer.Write( leaseset.Destination.IdentHash.Hash );
            writer.Write8( (byte)MessageContent.LeaseSet );
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

        public DatabaseStoreMessage( I2PLeaseSet leaseset ): this( leaseset, 0, null, 0 )
        {
        }

        I2PIdentHash CachedKey;
        MessageContent CachedContent;
        uint CachedReplyToken;
        uint CachedReplyTunnelId;
        I2PIdentHash CachedReplyGateway;
        I2PRouterInfo CachedRouterInfo;
        I2PLeaseSet CachedLeaseSet;

        public I2PIdentHash Key 
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedKey;
            }
        }

        public MessageContent Content
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedContent;
            }
        }

        public uint ReplyToken
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyToken;
            }
        }

        public uint ReplyTunnelId
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyTunnelId;
            }
        }

        public I2PIdentHash ReplyGateway
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyGateway;
            }
        }

        public I2PRouterInfo RouterInfo
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedRouterInfo;
            }
        }

        public I2PLeaseSet LeaseSet
        { 
            get 
            { 
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
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
            CachedKey = new I2PIdentHash( reader );
            CachedContent = reader.Read8() == 0 ? MessageContent.RouterInfo : MessageContent.LeaseSet;
            CachedReplyToken = reader.Read32();
            if ( CachedReplyToken != 0 )
            {
                CachedReplyTunnelId = reader.Read32();
                CachedReplyGateway = new I2PIdentHash( reader );
            }

            switch ( CachedContent )
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
                    /*
                case MessageContent.LeaseSet2:
                    break;

                case MessageContent.EncryptedLeaseSet:
                    break;

                case MessageContent.MetaLeaseSet:
                    break;
                    */
                default:
                    throw new InvalidDataException( $"DatabaseStoreMessage: {CachedContent} not supported" );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( $"DatabaseStore {Content}, key {Key.Id32Short}" );
            result.AppendLine( $"Reply token {ReplyToken}, tunnel {ReplyTunnelId}, GW {ReplyGateway}" );

            switch ( CachedContent )
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
