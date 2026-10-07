using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Spaarke.Scheduling.Tests;

/// <summary>Captures formatted log entries so a test can assert what the host said, and how often.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception)));

    public int Count(LogLevel level, string fragment) =>
        Entries.Count(e => e.Level == level && e.Message.Contains(fragment, StringComparison.Ordinal));
}
