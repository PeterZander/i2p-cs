using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    internal class SendBufferPool
    {
        const int MaxBufferSize = 1500;
        const int MaxNumberOfSendBuffers = 300;

        System.Buffers.ArrayPool<byte> Buffers = System.Buffers.ArrayPool<byte>.Create( MaxBufferSize, MaxNumberOfSendBuffers );

        internal SendBufferPool()
        {
        }

        internal BufLen Pop( int size )
        {
            return new BufLen( Buffers.Rent( size ), 0, size );
        }

        internal void Push( BufLen buf )
        {
            Buffers.Return( buf.BaseArray );
        }
    }
}
