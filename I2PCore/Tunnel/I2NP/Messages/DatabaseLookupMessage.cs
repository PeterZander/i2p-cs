using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class DatabaseLookupMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DatabaseLookup; } }

        [Flags]
        public enum LookupTypes: byte { 
            Tunnel = 0x01, 
            Encryption = 0x02,
            Normal = 0x00, 
            LeaseSet = 0x04, 
            RouterInfo = 0x08, 
            Exploration = 0x0C, 
        }

        I2PIdentHash CachedKey;
        public I2PIdentHash Key
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedKey;
            }
        }

        I2PIdentHash CachedFrom;
        public I2PIdentHash From
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedFrom;
            }
        }

        LookupTypes CachedLookupType;
        public LookupTypes LookupType
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedLookupType;
            }
        }

        I2PTunnelId CachedTunnelId;
        public I2PTunnelId TunnelId
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedTunnelId;
            }
        }

        List<DatabaseSearchReplyMessage> CachedExcludeList = new List<DatabaseSearchReplyMessage>();
        public List<DatabaseSearchReplyMessage> ExcludeList
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedExcludeList;
            }
        }

        I2PSessionKey CachedReplyKey;
        public I2PSessionKey ReplyKey
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedReplyKey;
            }
        }

        List<I2PSessionTag> CachedTags = new List<I2PSessionTag>();
        public List<I2PSessionTag> Tags
        {
            get
            {
                if ( CachedKey == null ) UpdateCachedFields( new BufRefLen( Payload ) );
                return CachedTags;
            }
        }

        public DatabaseLookupMessage( I2NPHeader header, BufRef reader )
        {
            var start = new BufRef( reader );
            UpdateCachedFields( reader );
            SetBuffer( start, reader );
        }

        public DatabaseLookupMessage( I2PIdentHash key, I2PIdentHash from, LookupTypes flags )
        {
            AllocateBuffer( 2 * 32 + 1 + 2 );
            var writer = new BufRefLen( Payload );

            writer.Write( key.Hash );
            writer.Write( from.Hash );
            writer.Write8( (byte)( (byte)flags & ~(byte)LookupTypes.Tunnel ) );
            writer.Write16( 0 );
        }

        public DatabaseLookupMessage( 
            I2PIdentHash key, 
            I2PIdentHash tunnelgw,
            I2PTunnelId tunnelid,
            LookupTypes flags, 
            IEnumerable<I2PIdentHash> excludelist )
        {
            var excludecount = excludelist == null ? 0 : excludelist.Count();

            AllocateBuffer( 2 * 32 + 1 + 4 + 2 + 32 * excludecount );
            var writer = new BufRefLen( Payload );

            writer.Write( key.Hash );
            writer.Write( tunnelgw.Hash );
            writer.Write8( (byte)( (byte)flags | (byte)LookupTypes.Tunnel ) );
            writer.Write32( tunnelid );

            if ( excludecount > 0 )
            {
                writer.WriteFlip16( (ushort)excludecount );
                foreach ( var addr in excludelist )
                {
                    writer.Write( addr.Hash );
                }
            }
            else
            {
                writer.Write16( 0 );
            }

            //dest.Add( 0 ); // Tags
        }

        public DatabaseLookupMessage( 
            I2PIdentHash key, 
            I2PIdentHash from, 
            LookupTypes flags, 
            IEnumerable<I2PIdentHash> excludelist )
        {
            var excludecount = excludelist == null ? 0 : excludelist.Count();

            AllocateBuffer( 2 * 32 + 1 + 2 + 32 * excludecount );
            var writer = new BufRefLen( Payload );

            writer.Write( key.Hash );
            writer.Write( from.Hash );
            writer.Write8( (byte)( (byte)flags & ~0x01 ) );

            if ( excludecount > 0 )
            {
                writer.WriteFlip16( (ushort)excludecount );
                foreach ( var addr in excludelist )
                {
                    writer.Write( addr.Hash );
                }
            }
            else
            {
                writer.Write16( 0 );
            }

            //dest.Add( 0 ); // Tags
        }

        void UpdateCachedFields( BufRef reader )
        {
            CachedKey = new I2PIdentHash( reader );
            CachedFrom = new I2PIdentHash( reader );
            CachedLookupType = (LookupTypes)reader.Read8();
            if ( ( (byte)CachedLookupType & 0x01 ) != 0 ) CachedTunnelId = new I2PTunnelId( reader );

            var excludecount = reader.ReadFlip16();
            for ( int i = 0; i < excludecount; ++i )
            {
                CachedExcludeList.Add( new DatabaseSearchReplyMessage( reader ) );
            }

            if ( ( (byte)CachedLookupType & 0x02 ) != 0 )
            {
                CachedReplyKey = new I2PSessionKey( reader );

                var tagcount = reader.Read8();
                CachedTags.Add( new I2PSessionTag( reader ) );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "DatabaseLookup" );

            return result.ToString();
        }
    }
}
