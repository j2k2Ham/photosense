using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Scanning;

public class InMemoryScanLogSink : IScanLogSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime,string,string)>> _logs = new();
    internal static readonly ConcurrentQueue<(string instanceId, DateTime ts, string level, string message)> PendingBroadcast = new();
    public void Log(string instanceId, string level, string message)
    {
        var q = _logs.GetOrAdd(instanceId, _ => new ConcurrentQueue<(DateTime,string,string)>());
        q.Enqueue((DateTime.UtcNow, level, message));
    // Bound per-instance log size
    while (q.Count > 400 && q.TryDequeue(out _)) { /* trim */ }
        PendingBroadcast.Enqueue((instanceId, DateTime.UtcNow, level, message));
    }
    public IReadOnlyList<(DateTime ts,string level,string message)> GetRecent(string instanceId, int max = 200)
    {
        if (!_logs.TryGetValue(instanceId, out var q)) return Array.Empty<(DateTime,string,string)>();
        return q.Reverse().Take(max).Reverse().ToList();
    }
}
