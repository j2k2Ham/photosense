using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Scanning;

public class InMemoryScanLogSink : IScanLogSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime,string,string)>> _logs = new();
    private static readonly ConcurrentQueue<(string instanceId, DateTime ts, string level, string message)> _pendingBroadcast = new();
    private readonly TimeProvider _time;

    public InMemoryScanLogSink(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void Log(string instanceId, string level, string message)
    {
        var q = _logs.GetOrAdd(instanceId, _ => new ConcurrentQueue<(DateTime,string,string)>());
        var now = _time.GetUtcNow().UtcDateTime;
        q.Enqueue((now, level, message));
    // Bound per-instance log size
    while (q.Count > 400 && q.TryDequeue(out _)) { /* trim */ }
        _pendingBroadcast.Enqueue((instanceId, now, level, message));
    }
    public IReadOnlyList<(DateTime ts,string level,string message)> GetRecent(string instanceId, int max = 200)
    {
        if (!_logs.TryGetValue(instanceId, out var q)) return Array.Empty<(DateTime,string,string)>();
        return q.Reverse().Take(max).Reverse().ToList();
    }
    /// <summary>
    /// Attempts to dequeue a pending broadcast log entry produced since last poll.
    /// </summary>
    public static bool TryDequeuePending(out (string instanceId, DateTime ts, string level, string message) item)
        => _pendingBroadcast.TryDequeue(out item);
}
