using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Crypto.Parameters;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PHashSHA256 : I2PType
    {
        enum BuildMode { Constructor, BatchList, Signed }
        BuildMode Mode;

        public byte[] Hash;

        List<I2PType> Batch;

        public I2PHashSHA256()
        {
            Mode = BuildMode.BatchList;
            Batch = new List<I2PType>();
        }

        public I2PHashSHA256( byte[] buf )
        {
            Mode = BuildMode.Constructor;

            Hash = DoSign( buf );
        }

        private byte[] DoSign( byte[] buf )
        {
            return GetHash( buf, 0, buf.Length );
        }

        public static byte[] GetHash( params BufLen[] bufs )
        {
            var sha = new Sha256Digest();
            foreach ( var buf in bufs )
            {
                sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            }
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );
            return hash;
        }

        public static byte[] GetHash( BufLen buf )
        {
            var sha = new Sha256Digest();
            sha.BlockUpdate( buf.BaseArray, buf.BaseArrayOffset, buf.Length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );
            return hash;
        }

        public static byte[] GetHash( byte[] buf )
        {
            return GetHash( buf, 0, buf.Length );
        }

        public static byte[] GetHash( byte[] buf, int offset, int length )
        {
            var sha = new Sha256Digest();
            sha.BlockUpdate( buf, offset, length );
            var hash = new byte[sha.GetDigestSize()];
            sha.DoFinal( hash, 0 );
            return hash;
        }

        public void Add( I2PType data )
        {
            if ( Mode != BuildMode.BatchList ) throw new InvalidOperationException( "Cannot mix build modes" );
            Batch.Add( data );
        }

        byte[] SignedData = null;
        public void Sign()
        {
            if ( Mode != BuildMode.BatchList ) throw new InvalidOperationException( "Cannot mix build modes" );

            var buf = new List<byte>();
            foreach ( var data in Batch )
            {
                data.Write( buf );
            }

            SignedData = buf.ToArray();
            Hash = DoSign( SignedData );

            Mode = BuildMode.Signed;
        }

        public bool Verify( byte[] buf, int offset, int length )
        {
            if ( Mode != BuildMode.Signed ) throw new InvalidOperationException( "No signature available" );

            var hash = GetHash( buf, offset, length );
            return BufUtils.Equals( Hash, hash );
        }

        public void Write( List<byte> dest )
        {
            if ( SignedData == null ) throw new InvalidOperationException( "No signed data available" );
            dest.AddRange( SignedData );
            dest.AddRange( Hash );
        }

        public void WriteSigOnly( List<byte> dest )
        {
            dest.AddRange( Hash );
        }

        public void WriteContentOnly( List<byte> dest )
        {
            dest.AddRange( SignedData );
        }
    }
}
