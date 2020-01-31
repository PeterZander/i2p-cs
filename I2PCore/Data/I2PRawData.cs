using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PRawData: I2PType
    {
        public byte[] Data;

        public I2PRawData( int bytes, bool random )
        {
            Data = random ? BufUtils.Random( bytes ): new byte[bytes];
        }

        public I2PRawData( byte b )
        {
            Data = new byte[] { b };
        }

        public I2PRawData( ushort ui )
        {
            Data = BitConverter.GetBytes( ui );
        }

        public I2PRawData( uint ui )
        {
            Data = BitConverter.GetBytes( ui );
        }

        public I2PRawData( byte[] buf, bool copy )
        {
            Data = copy ? buf.Copy( 0, buf.Length ): buf;
        }

        public I2PRawData( I2PRawData src, bool copy )
        {
            Data = copy ? src.Data.Copy( 0, src.Data.Length ) : src.Data;
        }

        public I2PRawData( BufRef reader, int size )
        {
            Data = new byte[size];
            Array.Copy( reader.BaseArray, reader.BaseArrayOffset, Data, 0, size );
            reader.Seek( size );
        }

        public void Write( BufRefStream dest )
        {
            dest.Write( Data );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PRawData" );

            if ( Data == null )
            {
                result.AppendLine( "Data: (null)" );
            }

            return result.ToString();
        }
    }
}
