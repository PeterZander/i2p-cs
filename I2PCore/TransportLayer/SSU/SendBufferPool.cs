using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.TransportLayer.SSU
{
    internal class SendBufferPool
    {
        const int MaxNumberOfSendBuffers = 300; 

        LinkedList<BufLen> Buffers = new LinkedList<BufLen>();

        internal SendBufferPool()
        {
            for ( int i = 0; i < 20; ++i )
            {
                Buffers.AddFirst( AllocateNewBuffer() );
            }
        }

        internal BufLen Pop()
        {
            lock ( Buffers )
            {
                if ( Buffers.Count == 0 )
                {
                    return AllocateNewBuffer();
                }

                var result = Buffers.Last.Value;
                Buffers.RemoveLast();
                return result;
            }
        }

        private static BufLen AllocateNewBuffer()
        {
            return new BufLen( new byte[MTUConfig.BufferSize] );
        }

        internal void Push( BufLen buf )
        {
            if ( buf.BaseArray.Length != MTUConfig.BufferSize ) return;
            lock ( Buffers )
            {
                if ( Buffers.Count > MaxNumberOfSendBuffers ) return;
                Array.Clear( buf.BaseArray, 0, buf.Length );
                Buffers.AddFirst( new BufLen( buf.BaseArray ) );
            }
        }
    }
}
