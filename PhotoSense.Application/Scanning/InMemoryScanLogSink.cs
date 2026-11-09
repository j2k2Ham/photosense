using System.Collections.Concurrent;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Application.Scanning;

/// <summary>A scan's lines, each waiting until the page next asks for them.</summary>
public class InMemoryScanLogSink : IScanLogSink
{
    private static readonly ConcurrentQueue<(string instanceId, DateTime ts, string level, string message)> _pendingBroadcast = new();
    private readonly TimeProvider _time;

    /// <summary>Lines stamped with the time from <paramref name="time"/>, or the system's clock when none is given.</summary>
    public InMemoryScanLogSink(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public void Log(string instanceId, string level, string message)
        => _pendingBroadcast.Enqueue((instanceId, _time.GetUtcNow().UtcDateTime, level, message));

    /// <summary>The oldest line not yet handed over, if there is one; each line is handed over once.</summary>
    public static bool TryDequeuePending(out (string instanceId, DateTime ts, string level, string message) item)
        => _pendingBroadcast.TryDequeue(out item);
}
