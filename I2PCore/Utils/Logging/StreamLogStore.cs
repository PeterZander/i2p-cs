using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace I2PCore.Utils
{
    public class StreamLogStore : ILogStore
    {
        protected StreamWriter LogFile { get; private set; } = null;

        public Stream Stream
        {
            set
            {
                Close();
                LogFile = new StreamWriter( value );
            }
        }

        public virtual string Name
        {
            get
            {
                return null;
            }

            set
            {
            }
        }

        public virtual void CheckStoreRotation()
        {
        }

        public void Close()
        {
            if ( LogFile != null )
            {
                LogFile.Close();
                LogFile.Dispose();
            }
            LogFile = null;
        }

        public virtual void Log( string text )
        {
            LogFile.Write( $"{text}\r\n" );
            LogFile.Flush();
            LogFile.BaseStream.Flush();
        }
    }
}
