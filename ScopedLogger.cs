using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sharp.Xmpp
{
    /// <summary>
    /// <see cref="IScopedLoggers"/> interface definition.
    /// Used by <see cref="LogFactory"/> to create <see cref="ILogger"/>
    /// </summary>
    public interface IScopedLoggers
    {
        /// <summary>
        /// To create a <see cref="ILogger"/> based on the specified one.
        /// </summary>
        /// <param name="inner"><see cref="ILogger"/>ILogger object</param>
        /// <param name="loggerPrefix"><see cref="String"/>Prefix to use</param>
        /// <param name="logLevel"><see cref="LogLevel"/>Minimum Log level to use</param>
        /// <returns><see cref="ILogger"/> - ILogger object</returns>
        ILogger Create(ILogger inner, string loggerPrefix, LogLevel logLevel = LogLevel.Debug);

        /// <summary>
        /// Change Log Level for all loggers with the specified prefix.
        /// </summary>
        /// <param name="loggerPrefix">The prefix for the loggers to modify.</param>
        /// <param name="level">The new log level.</param>
        void SetLevel(string loggerPrefix, LogLevel level);

        /// <summary>
        /// Get current log level for the logger with the specified prefix.
        /// </summary>
        /// <param name="loggerPrefix">The prefix for the logger to query.</param>
        /// <returns>The current log level.</returns>
        LogLevel GetLevel(string loggerPrefix);
    }

    /// <summary>
    /// Based on <see cref="IScopedLoggers"/> interface.
    /// Permit to create <see cref="ScopedLogger"/> and change easily Log Level of all of them based on their prefix.
    /// </summary>
    public class ScopedLoggers : IScopedLoggers
    {
        // Thread-safe
        private readonly ConcurrentDictionary<string, LogLevel> _levels = new();

        /// <summary>
        /// To create a <see cref="ScopedLogger"/> (inherits from <see cref="ILogger"/>) based :
        /// - on the specified <see cref="ILogger"/> 
        /// - and on the specified prefix
        /// </summary>
        /// <param name="inner"><see cref="ILogger"/>ILogger object</param>
        /// <param name="loggerPrefix"><see cref="String"/>Prefix to use</param>
        /// <param name="logLevel"><see cref="LogLevel"/>Minimum Log level to use</param>
        /// <returns><see cref="ILogger"/> - <see cref="ScopedLogger"/> object</returns>
        public ILogger Create(ILogger inner, string loggerPrefix, LogLevel logLevel = LogLevel.Debug)
        {
            _levels.AddOrUpdate(loggerPrefix, logLevel, (_, _) => logLevel);
            return new ScopedLogger(inner, loggerPrefix, this);
        }

        /// <summary>
        /// Change log level for all loggers with the specified prefix.
        /// </summary>
        /// <param name="loggerPrefix">The prefix for the loggers to modify.</param>
        /// <param name="level">The new log level.</param>
        public void SetLevel(string loggerPrefix, LogLevel level)
        {
            _levels[loggerPrefix] = level;
        }

        /// <summary>
        /// Get current log level for the logger with the specified prefix.
        /// </summary>
        /// <param name="loggerPrefix">The prefix for the logger to query.</param>
        /// <returns>The current log level.</returns>
        public LogLevel GetLevel(string loggerPrefix) =>
            _levels.TryGetValue(loggerPrefix, out var level) ? level : LogLevel.Debug;
    }

    public class ScopedLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly string _loggerPrefix;
        private readonly IScopedLoggers _levels;
        private readonly Dictionary<string, object> _state;

        public ScopedLogger(ILogger inner, string loggerPrefix, IScopedLoggers levels)
        {
            _inner = inner;
            _loggerPrefix = loggerPrefix;
            _levels = levels;
            _state = new Dictionary<string, object> { ["LoggerPrefix"] = loggerPrefix };
        }

        public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // The level specific to this instance must be respected.
            var instanceLevel = _levels.GetLevel(_loggerPrefix);
            if (logLevel < instanceLevel)
                return false;

            // The settings of the underlying ILogger (e.g., global minimum level) are also respected.
            return _inner.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            // For security, if Log() is called immediatly without using IsEnabled
            if (!IsEnabled(logLevel))
                return;

            using (_inner.BeginScope(_state))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
