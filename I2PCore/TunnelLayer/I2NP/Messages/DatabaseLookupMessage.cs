﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.TunnelLayer.I2NP.Data;

namespace I2PCore.TunnelLayer.I2NP.Messages
{
    public class DatabaseLookupMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.DatabaseLookup; } }

        [Flags]
        public enum LookupTypes: byte { 
            Tunnel          = 0b00000001, 
            Encryption      = 0b00000010,
            Normal          = 0b00000000, 
            LeaseSet        = 0b00000100,
            RouterInfo      = 0b00001000,
            Exploration     = 0b00001100,
            ECIES           = 0b00010000,
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

        public DatabaseLookupMessage( BufRef reader )
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
            writer.Write8( (byte)( flags & ~LookupTypes.Tunnel ) );
            writer.Write16( 0 );
        }

        public DatabaseLookupMessage( 
            I2PIdentHash key, 
            I2PIdentHash tunnelgw,
            I2PTunnelId tunnelid,
            LookupTypes flags, 
            IEnumerable<I2PIdentHash> excludelist = null,
            DatabaseLookupKeyInfo keyinfo = null )
        {
            var excludecount = excludelist == null ? 0 : excludelist.Count();

            var keyandtagsize = keyinfo is null ? 0 : keyinfo.ReplyKey.Length + 1 + keyinfo.Tags.Sum( t => t.Length );

            AllocateBuffer( 2 * 32 + 1 + 4 + 2 + 32 * excludecount + keyandtagsize );
            var writer = new BufRefLen( Payload );

            writer.Write( key.Hash );
            writer.Write( tunnelgw.Hash );

            var forceflags = flags | LookupTypes.Tunnel;
            if ( keyinfo != null )
            {
                forceflags &= ~LookupTypes.Encryption;
                forceflags &= ~LookupTypes.ECIES;

                forceflags |= keyinfo.EncryptionFlag ? LookupTypes.Encryption : 0;
                forceflags |= keyinfo.ECIESFlag ? LookupTypes.ECIES : 0;
            }
            writer.Write8( (byte)forceflags );

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

            if ( keyinfo is null ) return;

            writer.Write( keyinfo.ReplyKey );
            writer.Write8( (byte)keyinfo.Tags.Length );
            foreach( var tag in keyinfo.Tags )
            {
                writer.Write( tag );
            }
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
            writer.Write8( (byte)( flags & ~LookupTypes.Tunnel ) );

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
        }

        void UpdateCachedFields( BufRef reader )
        {
            CachedKey = new I2PIdentHash( reader );
            CachedFrom = new I2PIdentHash( reader );
            CachedLookupType = (LookupTypes)reader.Read8();
            if ( ( CachedLookupType & LookupTypes.Tunnel ) != 0 ) CachedTunnelId = new I2PTunnelId( reader );

            var excludecount = reader.ReadFlip16();
            for ( int i = 0; i < excludecount; ++i )
            {
                CachedExcludeList.Add( new DatabaseSearchReplyMessage( reader ) );
            }

            if ( ( CachedLookupType & LookupTypes.Encryption ) != 0 )
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
