using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Application.Scanning;

public class InMemoryScanProgressStore : IScanProgressStore
{
    private readonly ConcurrentDictionary<string, MutableProgress> _store = new();
    private volatile string? _latest;

    public void ScanStarted(string instanceId)
    {
        var prog = new MutableProgress { InstanceId = instanceId, StartedUtc = DateTime.UtcNow };
        _store[instanceId] = prog;
        _latest = instanceId;
    }

    public void SetTotals(string instanceId, int primaryTotal, int secondaryTotal)
    {
        if (_store.TryGetValue(instanceId, out var p))
        {
            p.PrimaryTotal = primaryTotal;
            p.SecondaryTotal = secondaryTotal;
        }
    }

    public void IncrementProcessed(string instanceId, bool primary)
    {
        if (_store.TryGetValue(instanceId, out var p))
        {
            if (primary) Interlocked.Increment(ref p.PrimaryProcessed);
            else Interlocked.Increment(ref p.SecondaryProcessed);
        }
    }

    public void ScanCompleted(string instanceId)
    {
        if (_store.TryGetValue(instanceId, out var p))
            p.CompletedUtc = DateTime.UtcNow;
    }

    public ScanProgressSnapshot Get(string instanceId)
        => _store.TryGetValue(instanceId, out var p) ? p.ToSnapshot() : new ScanProgressSnapshot(instanceId, DateTime.MinValue, null, 0, 0, 0, 0);

    public ScanProgressSnapshot GetLatest()
        => _latest is null ? new ScanProgressSnapshot(string.Empty, DateTime.MinValue, null, 0, 0, 0, 0) : Get(_latest);

    private sealed class MutableProgress
    {
        public string InstanceId { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public int PrimaryTotal;
        public int PrimaryProcessed;
        public int SecondaryTotal;
        public int SecondaryProcessed;
        public ScanProgressSnapshot ToSnapshot() =>
            new(InstanceId, StartedUtc, CompletedUtc, PrimaryTotal, PrimaryProcessed, SecondaryTotal, SecondaryProcessed);
    }
}