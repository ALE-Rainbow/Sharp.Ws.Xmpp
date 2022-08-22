using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sharp.Xmpp
{
    /// <summary>
    /// Class used to manage LoggerFactory object
    /// 
    /// It permits to use any back-ends log provider based on Microsoft Extension Logging (MEL):
    /// <a href="https://github.com/getsentry/sentry-dotnet">Sentry</a> provider for the Sentry service
    /// <a href="https://github.com/serilog/serilog-framework-logging">Serilog</a> provider for the Serilog library
    /// <a href="https://github.com/elmahio/Elmah.Io.Extensions.Logging">elmah.io</a> provider for the elmah.io service
    /// <a href="https://github.com/imobile3/Loggr.Extensions.Logging">Loggr</a> provider for the Loggr service
    /// <a href="https://github.com/NLog/NLog.Extensions.Logging">NLog</a> provider for the NLog service
    /// <a href="https://github.com/mattwcole/gelf-extensions-logging">Graylog</a> provider for the Graylog service
    /// <a href="https://github.com/airbrake/sharpbrake#microsoftextensionslogging-integration">Sharpbrake</a> provider for the Sharpbrake service
    /// <a href="https://github.com/catalingavan/KissLog-net">KissLog.net</a> provider for the KissLog.net service
    /// 
    /// </summary>
    public class LogFactory
    {
        private ILoggerFactory _factory = NullLoggerFactory.Instance;
        private static LogFactory _appLog;

        /// <summary>
        /// Instance of this statc class
        /// </summary>
        public static LogFactory Instance
        {
            get
            {
                if (_appLog == null)
                {
                    _appLog = new LogFactory();
                }

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
            if (string.IsNullOrEmpty(prefix))
                return Instance._factory.CreateLogger(categoryName);
            else
                return Instance._factory.CreateLogger($"{prefix}{categoryName}");
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

        private LogFactory()
        { 
            
        }
    }
}
