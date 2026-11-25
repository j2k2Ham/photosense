using Microsoft.Extensions.Logging;

namespace Trovolve.Tests;

/// <summary>A line written to the log, as a test reads it.</summary>
/// <param name="Level">Its level.</param>
/// <param name="EventId">Its event id.</param>
/// <param name="Message">Its words, put together.</param>
/// <param name="Error">The error it was written with, if any.</param>
internal sealed record LoggedLine(LogLevel Level, int EventId, string Message, Exception? Error);

/// <summary>A log that keeps every line written to it, at every level, for a test to look at.</summary>
/// <typeparam name="T">The class it is the log of.</typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<LoggedLine> _lines = [];

    /// <summary>Every line written, in order.</summary>
    public IReadOnlyList<LoggedLine> Lines
    {
        get
        {
            lock (_lines) return [.. _lines];
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lines) _lines.Add(new LoggedLine(logLevel, eventId.Id, formatter(state, exception), exception));
    }
}
