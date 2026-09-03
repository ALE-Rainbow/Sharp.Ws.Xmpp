using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace Sharp.Xmpp
{
    /// <summary>
    /// Class used to manage LoggerFactory object
    /// 
    /// It permits to use any back-ends log provider based on Microsoft Extension Logging (MEL)
    /// </summary>
    public class LogFactory
    {
        private ILoggerFactory _factory = NullLoggerFactory.Instance;
        private IScopedLoggers _scopedLoggers = null;

        private static LogFactory _appLog;

        /// <summary>
        /// Instance of this statc class
        /// </summary>
        public static LogFactory Instance
        {
            get
            {
                _appLog ??= new LogFactory();

                return _appLog;
            }
        }

        /// <summary>
        ///  To create / get logger used to log specfic details about WebRTC.
        ///  
        /// It's the same than using **CreateLogger("WEBRTC")**
        /// </summary>
        /// <returns><see cref="ILogger"/> - ILogger interface</returns>
        public static ILogger CreateWebRTCLogger(string prefix = null) =>
            CreateLogger("WEBRTC", prefix);

        /// <summary>
        ///  To create / get logger using a category name
        /// </summary>
        /// <returns><see cref="ILogger"/> - ILogger interface</returns>
        public static ILogger CreateLogger(string categoryName, string prefix = null)
        {
            var inner = string.IsNullOrEmpty(prefix)
                ? Instance._factory.CreateLogger(categoryName)
                : Instance._factory.CreateLogger($"{prefix}{categoryName}");

            return (String.IsNullOrEmpty(prefix) || Instance._scopedLoggers is null)
                ? inner
                : Instance._scopedLoggers.Create(inner, prefix);
        }

        /// <summary>
        ///  To create / get logger using a type
        /// </summary>
        /// <returns><see cref="ILogger"/> - ILogger interface</returns>
        public static ILogger CreateLogger<T>(string prefix = null) =>
            CreateLogger(typeof(T).ToString(), prefix);

        /// <summary>
        /// To set the ILoggerFactory used for logging purpose.
        /// 
        /// This method must be called before to use the SDK
        /// </summary>
        /// <param name="factory"><see cref="ILoggerFactory"/> interface</param>
        public static void Set(ILoggerFactory factory)
        {
            Instance._factory = factory;
        }

        /// <summary>
        /// To set the <see cref="IScopedLoggers"/> used for logging purpose.
        /// 
        /// This method must be called before to use the SDK.
        /// 
        /// **Note:** By default use **Sharp.Xmpp.ScopedLoggers** object
        /// </summary>
        /// <param name="scopedLoggers"><see cref="IScopedLoggers"/> interface</param>
        public static void SetScopedLoggers(IScopedLoggers scopedLoggers)
        {
            Instance._scopedLoggers = scopedLoggers;
        }

        /// <summary>
        /// To change, at runtime, the minimum <see cref="LogLevel"/> for a given logger instance
        /// (identified by its prefix, as passed to <see cref="CreateLogger(string, string)"/>).
        /// 
        /// Has no effect if no <see cref="IScopedLoggers"/> has been set (see <see cref="SetScopedLoggers"/>),
        /// or if <paramref name="prefix"/> is null or empty.
        /// </summary>
        /// <param name="prefix"><see cref="string"/> Prefix identifying the logger instance</param>
        /// <param name="logLevel"><see cref="LogLevel"/> New minimum level for this instance</param>
        public static void SetLevel(string prefix, LogLevel logLevel)
        {
            if (string.IsNullOrEmpty(prefix))
                return;

            Instance._scopedLoggers?.SetLevel(prefix, logLevel);
        }

        /// <summary>
        /// To get the current minimum <see cref="LogLevel"/> configured for a given logger instance
        /// (identified by its prefix).
        /// 
        /// Returns <see cref="LogLevel.Debug"/> as default if no <see cref="IScopedLoggers"/> has been set,
        /// or if <paramref name="prefix"/> is null or empty, or if no level was ever set for this prefix.
        /// </summary>
        /// <param name="prefix"><see cref="string"/> Prefix identifying the logger instance</param>
        /// <returns><see cref="LogLevel"/> - Current minimum level for this instance</returns>
        public static LogLevel GetLevel(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || Instance._scopedLoggers is null)
                return LogLevel.Debug;

            return Instance._scopedLoggers.GetLevel(prefix);
        }

        private LogFactory()
        { 
            
        }
    }
}
