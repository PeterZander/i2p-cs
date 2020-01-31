using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Utilities.Encoders;

namespace I2PCore.Tunnel.I2NP.Data
{
    public class BuildResponseRecord: I2PType
    {
        public enum RequestResponse : byte { Accept = 0, ProbabalisticReject = 10, TransientOverload = 20, Bandwidth = 30, Critical = 50 }
        public const RequestResponse DefaultErrorReply = RequestResponse.Bandwidth;

        public int Length { get { return 528; } }
        BufLen Data;

        public BuildResponseRecord( BufRef buf )
        {
            Data = buf.ReadBufLen( Length );
        }

        public BuildResponseRecord( BufLen src )
        {
            if ( src.Length != Length ) throw new ArgumentException( "BuildResponseRecord needs a 528 byte record!" );
            Data = src;
        }

        public BuildResponseRecord( EGBuildRequestRecord request )
        {
            // Replace it
            Data = request.Data;
            Data.Randomize();
        }

        public BuildResponseRecord( AesEGBuildRequestRecord request )
        {
            // Reuse it
            Data = request.Data;
        }

        public RequestResponse Reply
        {
            get { return (RequestResponse)Data[527]; }
            set { Data[527] = (byte)value; }
        }

        public BufLen Payload
        {
            get { return new BufLen( Data, 0, Length ); }
        }

        public BufLen Hash
        {
            get { return new BufLen( Data, 0, 32 ); }
        }

        public BufLen HashedArea
        {
            get { return new BufLen( Data, 32, 496 ); }
        }

        public bool CheckHash()
        {
            var hash = I2PHashSHA256.GetHash( HashedArea );
            return Hash.Equals( hash );
        }

        public void UpdateHash()
        {
            var hash = I2PHashSHA256.GetHash( HashedArea );
            Hash.Poke( new BufLen( hash ), 0 );
        }

        public bool IsDestination( I2PIdentHash comp )
        {
            return comp.Hash16 == Data;
        }

        public void Write( BufRefStream dest )
        {
            Data.WriteTo( dest );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.Append( "BuildResponseRecord" );

            if ( Data == null )
            {
                result.Append( " Content: (null)" );
            }
            else
            {
                result.Append( " Content: Reply: " + Reply.ToString() );
            }

            return result.ToString();
        }
    }
}
