using System;
using System.Diagnostics;
using System.Threading;
using CM = System.Configuration.ConfigurationManager;

namespace I2PCore.Utils
{
    public static class Logging
    {
        static ILogStore Store = null;

        static object Lock = new object();

        public enum LogLevels : int
        {
            Everything = 0,
            DebugData = 1,
            Debug = 5,
            Information = 20,
            Warning = 50,
            Error = 100,
            Critical = 500,
            Nothing = int.MaxValue
        }

        public static void ReadAppConfig()
        {
            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["LogFileLevel"] ) )
            {
                Logging.SetLogLevel( CM.AppSettings["LogFileLevel"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["LogFileNameTimeStamp"] ) )
            {
                Logging.TimestampFiles = bool.Parse( CM.AppSettings["LogFileNameTimeStamp"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["LogFileMaxBytes"] ) )
            {
                Logging.MaxLogFileSize = long.Parse( CM.AppSettings["LogFileMaxBytes"] );
            }

            if ( !string.IsNullOrWhiteSpace( CM.AppSettings["LogFileName"] ) )
            {
                Logging.LogToFile( CM.AppSettings["LogFileName"] );
            }
        }

        public static void SetLogLevel( LogLevels level )
        {
            LogLevel = level;
        }

        public static void SetLogLevel( string name )
        {
            LogLevel = (LogLevels)Enum.Parse( typeof( LogLevels ), name );
        }

#if DEBUG
        public static LogLevels LogLevel = LogLevels.DebugData;
#elif TRACE
        public static LogLevels LogLevel = LogLevels.Information;
#else
        public static LogLevels LogLevel = LogLevels.Warning;
#endif

        public static bool LogToConsole = false;
        public static bool LogToDebug = false;

        public static bool TimestampFiles = false;
        public static long MaxLogFileSize = 10 * 1024 * 1024;

        public static void LogToStore( ILogStore dest, string name )
        {
            lock ( Lock )
            {
                if ( Store != null )
                {
                    Store.Close();
                    Store = null;
                }

                Store = dest;
                Store.Name = name;
            }
        }

        public static void LogToFile( string filename )
        {
            LogToStore( new FileLogStore( TimestampFiles, MaxLogFileSize ), filename );
        }

        private static void CloseLogFile()
        {
            lock ( Lock )
            {
                if ( Store != null )
                {
                    Store.Close();
                    Store = null;
                }
            }
        }

        internal static string Unwrap( Exception ex )
        {
            return ex.ToString();
        }

        [Conditional( "DEBUG" )]
        public static void Log( string txt )
        {
            Log( LogLevels.DebugData, () => txt );
        }

        [Conditional( "DEBUG" )]
        public static void Log( Func<string> txtgen )
        {
            Log( LogLevels.DebugData, txtgen );
        }

        [Conditional( "DEBUG" )]
        public static void LogDebug( string txt )
        {
            Log( LogLevels.Debug, () => txt );
        }

        [Conditional( "DEBUG" )]
        public static void LogDebugData( string txt )
        {
            Log( LogLevels.DebugData, () => txt );
        }

        [Conditional( "DEBUG" )]
        public static void LogDebug( Func<string> txtgen )
        {
            Log( LogLevels.Debug, txtgen );
        }

        public static void LogInformation( string txt )
        {
            Log( LogLevels.Information, () => txt );
        }

        public static void LogWarning( string txt )
        {
            Log( LogLevels.Warning, () => txt );
        }

        public static void LogWarning( Exception ex )
        {
            Log( LogLevels.Warning, () => $"Exception: {Unwrap(ex)}" );
        }

        public static void LogWarning( string module, Exception ex )
        {
            Log( LogLevels.Warning, () => $"Exception ({module}): {Unwrap(ex)}" );
        }

        public static void LogCritical( string txt )
        {
            Log( LogLevels.Critical, () => txt );
        }

        public static void LogCritical( Exception ex )
        {
            Log( LogLevels.Critical, () => $"Exception: {Unwrap(ex)}" );
        }

        public static void LogCritical( string module, Exception ex )
        {
            Log( LogLevels.Critical, () => $"Exception ({module}): {Unwrap(ex)}" );
        }

        static PeriodicAction CheckFileRotation = new PeriodicAction( TickSpan.Minutes( 1 ) );
        static readonly char[] TrimEndChars = new char[] { '\r', '\n', ' ', '\t' };

        public static void Log( LogLevels level, Func<string> txtgen )
        {
            if ( level < LogLevel ) return;

            var st = $"{DateTime.Now} /{Thread.CurrentThread.ManagedThreadId,3}/: {txtgen().TrimEnd( TrimEndChars )}";

            lock ( Lock )
            {
                if ( LogToConsole ) Console.WriteLine( st );
                if ( LogToDebug ) System.Diagnostics.Debug.WriteLine( st );

                if ( Store != null )
                {
                    CheckFileRotation.Do( () => Store.CheckStoreRotation() );
                    Store.Log( st );
                }
            }
        }

        public static void Log( Exception ex )
        {
            LogWarning( $"Exception: {Unwrap(ex)}" );
        }

        public static void Log( string module, Exception ex )
        {
            LogWarning( $"Exception ({module}): {Unwrap(ex)}" );
        }

        [Conditional( "DEBUG" )]
        public static void LogDebug( Exception ex )
        {
            LogDebug( $"Exception: {Unwrap(ex)}" );
        }

        [Conditional( "DEBUG" )]
        public static void LogDebug( string module, Exception ex )
        {
            LogDebug( $"Exception ({module}): {Unwrap(ex)}" );
        }
    }
}
