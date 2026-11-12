using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Application.Scanning;

/// <summary>How far each scan of this run of the service has got, kept in memory only: a scan cut short by a stop is not resumed.</summary>
public class InMemoryScanProgressStore : IScanProgressStore
{
    private readonly ConcurrentDictionary<string, MutableProgress> _store = new();
    private volatile string? _latest;
    private readonly TimeProvider _time;

    /// <summary>Progress stamped with the time from <paramref name="time"/>.</summary>
    public InMemoryScanProgressStore(TimeProvider time) => _time = time;

    /// <inheritdoc />
    public void ScanStarted(string instanceId)
    {
        var prog = new MutableProgress { InstanceId = instanceId, StartedUtc = _time.GetUtcNow().UtcDateTime };
        _store[instanceId] = prog;
        _latest = instanceId;
    }

    /// <inheritdoc />
    public void SetTotals(string instanceId, int primaryTotal, int secondaryTotal)
    {
        if (_store.TryGetValue(instanceId, out var p))
        {
            p.PrimaryTotal = primaryTotal;
            p.SecondaryTotal = secondaryTotal;
        }
    }

    /// <inheritdoc />
    public void Expect(string instanceId, double? seconds)
    {
        if (_store.TryGetValue(instanceId, out var p)) p.ExpectedSeconds = seconds;
    }

    /// <inheritdoc />
    public void IncrementProcessed(string instanceId, bool primary)
    {
        if (_store.TryGetValue(instanceId, out var p))
        {
            if (primary) Interlocked.Increment(ref p.PrimaryProcessed);
            else Interlocked.Increment(ref p.SecondaryProcessed);
        }
    }

    /// <inheritdoc />
    public void Read(string instanceId, string photoId)
    {
        if (_store.TryGetValue(instanceId, out var p))
            lock (p.Recent)
            {
                p.Recent.Enqueue(photoId);
                if (p.Recent.Count > ScanProgressSnapshot.RecentKept) p.Recent.Dequeue();
            }
    }

    /// <inheritdoc />
    public void ScanCompleted(string instanceId)
    {
        if (_store.TryGetValue(instanceId, out var p))
            p.CompletedUtc = _time.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public ScanProgressSnapshot Get(string instanceId)
        => _store.TryGetValue(instanceId, out var p) ? p.ToSnapshot() : new ScanProgressSnapshot(instanceId, DateTime.MinValue, null, 0, 0, 0, 0);

    /// <inheritdoc />
    public ScanProgressSnapshot GetLatest()
        => _latest is null ? new ScanProgressSnapshot(string.Empty, DateTime.MinValue, null, 0, 0, 0, 0) : Get(_latest);

    private sealed class MutableProgress
    {
        /// <summary>The scan.</summary>
        public string InstanceId { get; set; } = "";
        /// <summary>When it began.</summary>
        public DateTime StartedUtc { get; set; }
        /// <summary>When it ended; null while it runs.</summary>
        public DateTime? CompletedUtc { get; set; }
        /// <summary>Files in the first folder.</summary>
        public int PrimaryTotal;
        /// <summary>Of those, how many have been gone through.</summary>
        public int PrimaryProcessed;
        /// <summary>Files in the second folder.</summary>
        public int SecondaryTotal;
        /// <summary>Of those, how many have been gone through.</summary>
        public int SecondaryProcessed;
        /// <summary>How long earlier scans suggest it will take in all.</summary>
        public double? ExpectedSeconds;
        /// <summary>The latest pictures read that have a preview, oldest first; locked while used, as files are read in parallel.</summary>
        public readonly Queue<string> Recent = new();
        /// <summary>How far it has got, as it stands now.</summary>
        public ScanProgressSnapshot ToSnapshot()
        {
            lock (Recent)
                return new(InstanceId, StartedUtc, CompletedUtc, PrimaryTotal, PrimaryProcessed, SecondaryTotal, SecondaryProcessed) { ExpectedSeconds = ExpectedSeconds, Recent = [.. Recent] };
        }
    }
}
