using System.Collections.Concurrent;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Application.Scanning;

/// <summary>A scan's lines, each waiting until the page next asks for them.</summary>
/// <param name="time">The clock each line is stamped with.</param>
public sealed class InMemoryScanLogSink(TimeProvider time) : IScanLogSink
{
    private readonly ConcurrentQueue<ScanLogLine> _pending = new();

    /// <inheritdoc />
    public void Log(string instanceId, string level, string message)
        => _pending.Enqueue(new ScanLogLine(instanceId, time.GetUtcNow().UtcDateTime, level, message));

    /// <inheritdoc />
    public IReadOnlyList<ScanLogLine> TakePending(int max)
    {
        var lines = new List<ScanLogLine>();
        while (lines.Count < max && _pending.TryDequeue(out var line)) lines.Add(line);
        return lines;
    }
}
