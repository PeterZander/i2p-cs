using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2P.Streaming
{
    public class StreamingPacket
    {
        public const int MTU = 1730;

        [Flags]
        public enum PacketFlags : ushort {
            SYNCHRONIZE = 1 << 0,
            CLOSE = 1 << 1,
            RESET = 1 << 2,
            SIGNATURE_INCLUDED = 1 << 3,
            SIGNATURE_REQUESTED = 1 << 4,
            FROM_INCLUDED = 1 << 5,
            DELAY_REQUESTED = 1 << 6,
            MAX_PACKET_SIZE_INCLUDED = 1 << 7,
            PROFILE_INTERACTIVE = 1 << 8,
            ECHO = 1 << 9,
            NO_ACK = 1 << 10,
            OFFLINE_SIGNATURE = 1 << 11,
        }

        public uint ReceiveStreamId;
        public uint SendStreamId;
        public uint SequenceNumber;
        public uint AckTrhough;
        public List<uint> NACKs;
        public byte ResendDelay;
        public PacketFlags Flags;
        public BufLen Payload;

        public I2PDestination From;
        public I2PSigningPrivateKey SigningKey;
        public I2PSignature Signature;

        public StreamingPacket( PacketFlags flags )
        {
            Flags = flags;
        }

        public StreamingPacket( BufRefLen reader )
        {
            SendStreamId = reader.ReadFlip32();
            ReceiveStreamId = reader.ReadFlip32();
            SequenceNumber = reader.ReadFlip32();
            AckTrhough = reader.ReadFlip32();

            NACKs = new List<uint>();
            var nackcount = reader.Read8();
            for ( int i = 0; i < nackcount; ++i )
            {
                NACKs.Add( reader.ReadFlip32() );
            }

            ResendDelay = reader.Read8();

            Flags = (PacketFlags)reader.ReadFlip16();
            var optionsize = reader.ReadFlip16();

            // Options order
            // DELAY_REQUESTED
            // FROM_INCLUDED
            if ( ( Flags & PacketFlags.FROM_INCLUDED ) != 0 )
            {
                From = new I2PDestination( reader );
            }
            // MAX_PACKET_SIZE_INCLUDED
            if ( ( Flags & PacketFlags.MAX_PACKET_SIZE_INCLUDED ) != 0 )
            {
                var mtu = reader.ReadFlip16();
            }
            // OFFLINE_SIGNATURE
            // SIGNATURE_INCLUDED
            if ( ( Flags & PacketFlags.SIGNATURE_INCLUDED ) != 0 )
            {
                Signature = new I2PSignature( reader, From.Certificate );
            }

            Payload = reader.ReadBufLen( reader.Length );
        }

        public void Write( BufRefStream dest )
        {
            // Not including options
            var headersize = 4 * 4 + 1 + NACKs.Count * 4 + 1 + 2 + 2;

            // Options
            var optionssize = ( Flags & PacketFlags.FROM_INCLUDED ) != 0 
                ? From.Size : 0;

            optionssize += ( Flags & PacketFlags.SIGNATURE_INCLUDED ) != 0
                ? From.SigningPublicKey.Certificate.SignatureLength
                : 0;

            optionssize += ( Flags & PacketFlags.MAX_PACKET_SIZE_INCLUDED ) != 0
                ? 2 : 0;

            optionssize += ( Flags & PacketFlags.DELAY_REQUESTED ) != 0
                ? 2 : 0;

            var header = new BufLen( new byte[headersize + optionssize] );
            var writer = new BufRefLen( header );

            writer.WriteFlip32( SendStreamId );
            writer.WriteFlip32( ReceiveStreamId );
            writer.WriteFlip32( SequenceNumber );
            writer.WriteFlip32( AckTrhough );

            writer.Write8( (byte)NACKs.Count );
            foreach ( var nak in NACKs )
            {
                writer.WriteFlip32( nak );
            }

            writer.Write8( ResendDelay );

            writer.WriteFlip16( (ushort)Flags );
            writer.WriteFlip16( (ushort)optionssize );

            // Options order
            // DELAY_REQUESTED
            // FROM_INCLUDED
            if ( ( Flags & PacketFlags.FROM_INCLUDED ) != 0 )
            {
                writer.Write( From.ToByteArray() );
            }
            // MAX_PACKET_SIZE_INCLUDED
            if ( ( Flags & PacketFlags.MAX_PACKET_SIZE_INCLUDED ) != 0 )
            {
                writer.WriteFlip16( MTU );
            }
            // OFFLINE_SIGNATURE
            // SIGNATURE_INCLUDED
            if ( ( Flags & PacketFlags.SIGNATURE_INCLUDED ) != 0 )
            {
                writer.Write( I2PSignature.DoSign( SigningKey, header ) );
            }

#if DEBUG
            if ( writer.Length != 0 )
            {
                throw new InvalidOperationException( "StreamingPacket Write buffer size error" );
            }
#endif

            dest.Write( (BufRefLen)header );
            dest.Write( (BufRefLen)Payload );
        }

        public override string ToString()
        {
            return $"{GetType().Name} {Flags} {From?.IdentHash.Id32Short} {ReceiveStreamId} {SendStreamId} {SequenceNumber} {AckTrhough}";
        }
    }
}
