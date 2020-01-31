using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace I2PCore.Utils
{
    public class FileLogStore : StreamLogStore
    {
        readonly public long MaxLogFileSize;

        string LogFileName = null;
        DateTime LogFileCreated;
        readonly bool DoTimestamp;

        public FileLogStore( bool dotimestamp, long maxfilesize = 10 * 1024 * 1024 )
        {
            DoTimestamp = dotimestamp;
            MaxLogFileSize = maxfilesize;
        }

        public override string Name
        {
            get
            {
                return LogFileName;
            }

            set
            {
                Close();
                LogFileName = Path.GetFullPath( value );
            }
        }

        protected virtual void CreateTheFile()
        {
            var dirname = Path.GetDirectoryName( LogFileName );
            if ( !Directory.Exists( dirname ) )
                Directory.CreateDirectory( dirname );

            var retries = 1;
        again:
            try
            {
                string tsfilename;

                if ( DoTimestamp )
                {
                    tsfilename = Path.Combine( Path.GetDirectoryName( LogFileName ),
                        Path.GetFileNameWithoutExtension( LogFileName )
                        + $"_{DateTime.UtcNow:yyyyMMdd_HHmm}{retries}{Path.GetExtension( LogFileName )}" );
                    Stream = new FileStream(
                            tsfilename,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.Read,
                            1024 );
                }
                else
                {
                    tsfilename = Path.Combine( Path.GetDirectoryName( LogFileName ),
                        Path.GetFileNameWithoutExtension( LogFileName )
                        + $"{(retries == 1 ? "": retries.ToString())}{Path.GetExtension( LogFileName )}" );
                    Stream = new FileStream(
                            tsfilename,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.Read,
                            1024 );
                }
            }
            catch ( IOException ex )
            {
                Thread.Sleep( 200 );
                if ( retries++ <= 5 ) goto again;

                LogFileName = null;
                Close();

                Debug.WriteLine( ex );
                throw;
            }
            LogFileCreated = DateTime.UtcNow;
        }

        public override void CheckStoreRotation()
        {
            if ( LogFile == null ) return;

            if ( LogFile.BaseStream.Length > MaxLogFileSize || 
                ( DoTimestamp && LogFileCreated.Day != DateTime.UtcNow.Day ) )
            {
                Close();
            }
        }

        public override void Log( string text )
        {
            if ( LogFile == null ) CreateTheFile();
            base.Log( text );
        }
    }
}
