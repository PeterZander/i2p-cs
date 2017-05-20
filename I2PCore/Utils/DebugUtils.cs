using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace I2PCore.Utils
{
    public static class DebugUtils
    {
        static StreamWriter LogFile = null;
        static object Lock = new object();

        public enum LogLevels : int { Everything = 0, DebugData = 1, Debug = 5, Information = 20, Warning = 50, Error = 100, Critical = 500, Nothing = int.MaxValue }

#if DEBUG
        public static LogLevels LogLevel = LogLevels.DebugData;
#elif TRACE
        public static LogLevels LogLevel = LogLevels.Information;
#else
        public static LogLevels LogLevel = LogLevels.Warning;
#endif

        public static bool LogToConsole = false;
        public static bool LogToDebug = false;
        
        public static void LogToFile( string filename )
        {
            lock ( Lock )
            {
                CloseLogFile();
                LogFile = new StreamWriter( new FileStream( filename, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 ) );
            }
        }

        private static void CloseLogFile()
        {
            lock ( Lock )
            {
                if ( LogFile != null )
                {
                    LogFile.Close();
                    LogFile.Dispose();
                    LogFile = null;
                }
            }
        }

        public static void Log( string txt )
        {
            Log( LogLevels.DebugData, txt );
        }

        public static void LogDebug( string txt )
        {
            Log( LogLevels.Debug, txt );
        }

        public delegate string DebugTextGenerator();

        public static void LogDebug( DebugTextGenerator gen )
        {
            Log( LogLevels.Debug, gen() );
        }

        public static void LogInformation( string txt )
        {
            Log( LogLevels.Information, txt );
        }

        public static void LogWarning( string txt )
        {
            Log( LogLevels.Warning, txt );
        }

        public static void LogCritical( string txt )
        {
            Log( LogLevels.Critical, txt );
        }

        public static void Log( LogLevels level, string txt )
        {
            if ( level < LogLevel ) return;

            var st = String.Format( "{0} /{2,3}/: {1}", DateTime.Now, txt, Thread.CurrentThread.ManagedThreadId );
            lock ( Lock )
            {
                if ( LogToConsole ) Console.WriteLine( st );
                if ( LogToDebug ) System.Diagnostics.Debug.WriteLine( st );

                if ( LogFile != null )
                {
                    LogFile.Write( st );
                    LogFile.Write( "\r\n" );
                    LogFile.Flush();
                    LogFile.BaseStream.Flush();
                }
            }
        }

        public static void Log( Exception ex )
        {
            LogDebug( ex.ToString() );
        }

        public static void Log( string module, Exception ex )
        {
            LogDebug( module + ": " + ex.ToString() );
        }
    }
}
