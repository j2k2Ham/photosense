using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Scanning;

public class InMemoryScanLogSink : IScanLogSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime,string,string)>> _logs = new();
    public void Log(string instanceId, string level, string message)
    {
        var q = _logs.GetOrAdd(instanceId, _ => new ConcurrentQueue<(DateTime,string,string)>());
        q.Enqueue((DateTime.UtcNow, level, message));
        while (q.Count > 400 && q.TryDequeue(out _)) { }
    }
    public IReadOnlyList<(DateTime ts,string level,string message)> GetRecent(string instanceId, int max = 200)
    {
        if (!_logs.TryGetValue(instanceId, out var q)) return Array.Empty<(DateTime,string,string)>();
        return q.Reverse().Take(max).Reverse().ToList();
    }
}
