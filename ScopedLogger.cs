using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Sharp.Xmpp
{
    public interface IScopedLoggers
    {
        public ILogger Create(ILogger inner, string loggerPrefix);
    }

    public class ScopedLoggers : IScopedLoggers
    {
        public ILogger Create(ILogger inner, string loggerPrefix)
        {
            return new ScopedLogger(inner, loggerPrefix);
        }
    }

    public class ScopedLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly Dictionary<string, object> _state;

        public ScopedLogger(ILogger inner, string loggerPrefix)
        {
            _inner = inner;
            _state = new Dictionary<string, object> { ["LoggerPrefix"] = loggerPrefix };
        }

        public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            using (_inner.BeginScope(_state))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
