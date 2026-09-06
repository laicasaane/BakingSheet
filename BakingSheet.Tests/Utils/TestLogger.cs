// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Tests
{
    public class TestLogger : ILogger
    {
        private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

        public sealed class LogEntry
        {
            public LogLevel Level { get; set; }
            public IReadOnlyList<object> Scopes { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
            public IReadOnlyList<KeyValuePair<string, object>> State { get; set; }

            public override string ToString()
            {
                return $"[{Level}] [{string.Join(">", Scopes)}] {Message}";
            }
        }

        private List<LogEntry> entries = new List<LogEntry>();

        public IReadOnlyList<LogEntry> Entries => entries;

        public IDisposable BeginScope<TState>(TState state)
        {
            return scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var entry = new LogEntry
            {
                Level = logLevel,
                Scopes = new List<object>(),
                Message = formatter(state, exception),
                Exception = exception,
                State = state is IEnumerable<KeyValuePair<string, object>> values
                    ? values.ToList()
                    : Array.Empty<KeyValuePair<string, object>>()
            };

            scopeProvider.ForEachScope(
                (scope, scopes) => ((List<object>)scopes).Add(scope), entry.Scopes);

            entries.Add(entry);
        }

        public void VerifyLog(LogLevel logLevel, string message, object[] scopes = null)
        {
            Assert.Contains(entries, entry =>
                entry.Level == logLevel && entry.Message == message &&
                (scopes == null || scopes.SequenceEqual(entry.Scopes))
            );
        }

        public void VerifyLogCount(LogLevel logLevel, string message, int count)
        {
            Assert.Equal(count, entries.Count(entry => entry.Level == logLevel && entry.Message == message));
        }

        public void VerifyNoError()
        {
            Assert.DoesNotContain(entries, entry =>
                entry.Level >= LogLevel.Error &&
                !(entry.Message.StartsWith("Sheet property ", StringComparison.Ordinal) &&
                  entry.Message.Contains(" has no loaded sheet. Check the source sheet name and imported data.")));
        }
    }
}
