
using System;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLeaseSet2Header: I2PType
    {
        [Flags]
        public enum HeaderFlagTypes : ushort
        {
            None = 0x00,
            Invalid = ushort.MaxValue,
            OfflineKey = 0x01,
            Unpublished = 0x02,
            Blinded = 0x04,
        }

        public I2PDestination Destination { get; set; }
        public I2PDateShort Published { get; set; }
        public ushort ExpiresSeconds { get; set; }
        public HeaderFlagTypes Flags { get; set; }
        public I2POfflineSignature OfflineSignature { get; set; }
        public I2PLeaseSet2Header( 
                I2PDestination dest,
                I2PDateShort published,
                HeaderFlagTypes flags,
                ushort expiresseconds = I2PLease2.DefaultLeaseLifetimeSeconds )
        {
            if ( dest is null || published is null )
            {
                throw new ArgumentException( "Null argument not supported" );
            }
            Destination = dest;
            Published = published;
            Flags = flags;
            ExpiresSeconds = expiresseconds;
        }
        public I2PLeaseSet2Header( BufRef reader )
        {
            Destination = new I2PDestination( reader );
            Published = new I2PDateShort( reader );
            ExpiresSeconds = reader.ReadFlip16();
            Flags = (HeaderFlagTypes)reader.ReadFlip16();
            if ( Flags.HasFlag( HeaderFlagTypes.OfflineKey ) )
            {
                OfflineSignature = new I2POfflineSignature( reader, Destination.Certificate );
            }
        }
        public void Write( BufRefStream dest )
        {
            Destination.Write( dest );
            Published.Write( dest );
            dest.Write( BufUtils.Flip16B( ExpiresSeconds ) );
            dest.Write( BufUtils.Flip16B( (ushort)Flags ) );
            if ( Flags.HasFlag( HeaderFlagTypes.OfflineKey ) )
            {
                OfflineSignature?.Write( dest );
            }
        }
    }
}
