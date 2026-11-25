using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Trovolve.Application.Scanning.Interfaces;

namespace Trovolve.Application.Scanning;

/// <summary>A scan's lines, each waiting until the page next asks for them, and written to the program's log as they come.</summary>
/// <param name="time">The clock each line is stamped with.</param>
/// <param name="logger">The program's log, which keeps every line after the page has taken it.</param>
public sealed class InMemoryScanLogSink(TimeProvider time, ILogger<InMemoryScanLogSink> logger) : IScanLogSink
{
    private readonly ConcurrentQueue<ScanLogLine> _pending = new();

    /// <inheritdoc />
    public void Log(string instanceId, string level, string message)
    {
        _pending.Enqueue(new ScanLogLine(instanceId, time.GetUtcNow().UtcDateTime, level, message));
        TrovolveLog.PageLine(logger, LevelOf(level), instanceId, message);
    }

    // The page's levels as the log's: "Warn" from a scan, "Warning" from putting back what a stop cut short.
    private static LogLevel LevelOf(string level) => level switch
    {
        "Error" => LogLevel.Error,
        "Warn" or "Warning" => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    /// <inheritdoc />
    public IReadOnlyList<ScanLogLine> TakePending(int max)
    {
        var lines = new List<ScanLogLine>();
        while (lines.Count < max && _pending.TryDequeue(out var line)) lines.Add(line);
        return lines;
    }
}
