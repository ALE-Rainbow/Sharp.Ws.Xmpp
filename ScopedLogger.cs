using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
        /// <param name="scopes"><see cref="T:Dictionary{String, Object}"/>Scopes to use</param>
        /// <returns><see cref="ILogger"/> - ILogger object</returns>
        ILogger Create(ILogger inner, string loggerPrefix, LogLevel logLevel = LogLevel.Debug, Dictionary<string, object> scopes = null);

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
    /// Default class inheriting from <see cref="IScopedLoggers"/>.
    /// Permits to create <see cref="ScopedLogger"/>:
    /// - loggerPrefix will be automatically added to each log entry as "LoggerPrefix" scope
    /// - scopes specified will be automatically added to each log entry
    /// - logLevel can be easily changed at runtime for all loggers with the same prefix
    /// </summary>
    public class ScopedLoggers : IScopedLoggers
    {
        // Thread-safe
        private readonly ConcurrentDictionary<string, LogLevel> _levels = new();

        /// <summary>
        /// To create a <see cref="ScopedLogger"/> (inheriting from <see cref="ILogger"/>) based
        /// - on a <see cref="ILogger"/> 
        /// - on a loggerPrefix (used as "LoggerPrefix" scope)
        /// - on a logLevel (used to filter log entries)
        /// - on scopes (used as additional scopes for each log entry)
        /// </summary>
        /// <param name="inner"><see cref="ILogger"/>ILogger object</param>
        /// <param name="loggerPrefix"><see cref="String"/>Prefix to use</param>
        /// <param name="logLevel"><see cref="LogLevel"/>Minimum Log level to use</param>
        /// <param name="scopes"><see cref="T:Dictionary{String, Object}"/>Scopes to use</param>
        /// <returns><see cref="ILogger"/> - <see cref="ScopedLogger"/> object</returns>
        public ILogger Create(ILogger inner, string loggerPrefix, LogLevel logLevel = LogLevel.Debug, Dictionary<string, object> scopes = null)
        {
            _levels.TryAdd(loggerPrefix, logLevel);
            return new ScopedLogger(this, inner, loggerPrefix, scopes);
        }

        /// <summary>
        /// Change log level for all loggers with the specified prefix.
        /// 
        /// Note: 
        /// - the LogFactory configuration set a minimum log level for all loggers, which will be respected by the underlying <see cref="ILogger"/>.
        /// - this minimum level cannot be changed using this method.
        /// - it this minimum level is too high (for example LogLevel.Information), then this method will have no effect for log entries below this level (for example LogLevel.Debug).
        /// </summary>
        /// <param name="loggerPrefix"><see cref="String"/>The prefix for the loggers to modify.</param>
        /// <param name="level"><see cref="LogLevel"/>The new log level.</param>
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

    /// <summary>
    /// Class inheriting from <see cref="ILogger"/> created by <see cref="ScopedLoggers"/>
    /// - loggerPrefix will be automatically added to each log entry as "LoggerPrefix" scope
    /// - scopes specified will be automatically added to each log entry
    /// </summary>
    public class ScopedLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly string _loggerPrefix;
        private readonly IScopedLoggers _scopedLoggers;
        private readonly Dictionary<string, object> _state;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="scopedLoggers"><see cref="IScopedLoggers"/>IScopedLoggers object</param>
        /// <param name="inner"><see cref="ILogger"/>ILogger object</param>
        /// <param name="loggerPrefix"><see cref="String"/>Prefix to use</param>
        /// <param name="scopes"><see cref="T:Dictionary{String, Object}"/>Scopes to use</param>
        public ScopedLogger(IScopedLoggers scopedLoggers, ILogger inner, string loggerPrefix, Dictionary<string, object> scopes = null)
        {
            _inner = inner;
            _loggerPrefix = loggerPrefix;
            _scopedLoggers = scopedLoggers;
            _state = new Dictionary<string, object> { ["LoggerPrefix"] = loggerPrefix }; // <= this scope will be added each a log entry is added
            if (scopes != null) // <= We add automatically more scopes if any
            {
                foreach (var kvp in scopes)
                { 
                    if (!_state.ContainsKey(kvp.Key))
                        _state[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Begin a logical operation scope
        /// </summary>
        /// <param name="state">the type of the state to begin scope for</param>
        /// <returns><see cref="IDisposable"/>An IDisposable object thatn ends the logicla scope on dispose</returns>
        public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);

        /// <summary>
        /// Check if the given log level is enabled.
        /// </summary>
        /// <param name="logLevel"><see cref="LogLevel"/>The log level to check.</param>
        /// <returns><see cref="bool"/>True if the log level is enabled; otherwise, false.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            // The level specific to this instance must be respected.
            var instanceLevel = _scopedLoggers.GetLevel(_loggerPrefix);
            if (logLevel < instanceLevel)
                return false;

            // The settings of the underlying ILogger (e.g., global minimum level) are also respected.
            return _inner.IsEnabled(logLevel);
        }

        /// <summary>
        /// Logs a message with the specified log level, event ID, state, exception, and formatter.
        /// </summary>
        /// <param name="logLevel"><see cref="LogLevel"/>The log level to use.</param>
        /// <param name="eventId"><see cref="EventId"/>The event ID to use.</param>
        /// <param name="state"><see cref="TState"/>The state to use.</param>
        /// <param name="exception"><see cref="Exception"/>The exception to use.</param>
        /// <param name="formatter"><see cref="Func{TState, Exception, string}"/>The formatter to use.</param>
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
