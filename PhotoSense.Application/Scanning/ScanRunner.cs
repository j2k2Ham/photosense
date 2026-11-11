using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Application.Scanning;

/// <summary>
/// Runs a scan in the background of whichever program asks for one, one scan at a time. What it has
/// got to is kept in the progress store, which the status and progress addresses read.
/// </summary>
public sealed class ScanRunner
{
    private readonly IScanExecutionService _scans;
    private readonly IScanProgressStore _progress;
    private readonly IScanLogSink _log;
    // Requests are taken one at a time, so a second click cannot slip in before the first scan is marked as started.
    private readonly object _gate = new();
    // The scan running, and what stops it; both null once it has ended.
    private string? _current;
    private CancellationTokenSource? _stop;

    /// <summary>Runs the scans of <paramref name="scans"/>, with their progress in <paramref name="progress"/> and what stops one in <paramref name="log"/>.</summary>
    public ScanRunner(IScanExecutionService scans, IScanProgressStore progress, IScanLogSink log)
    {
        _scans = scans;
        _progress = progress;
        _log = log;
    }

    /// <summary>The scan last started, while it runs; a finished one stays here to be waited for.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>Whether the scan latest started is still running.</summary>
    public static bool IsRunning(ScanProgressSnapshot latest) => latest.InstanceId.Length > 0 && latest.CompletedUtc is null;

    /// <summary>Starts a scan and says what it is known by, or returns null when one is running already.</summary>
    public string? Start(ScanRequest request)
    {
        lock (_gate)
        {
            // Two scans at once would each forget the files the other had found.
            if (IsRunning(_progress.GetLatest())) return null;
            var instanceId = Guid.NewGuid().ToString("N");
            _progress.ScanStarted(instanceId);
            var stop = new CancellationTokenSource();
            (_current, _stop) = (instanceId, stop);
            // A scan runs on its own once started: the request that started it is answered at once, and only Stop ends it part way.
            Running = Task.Run(() => RunAsync(request, instanceId, stop), CancellationToken.None);
            return instanceId;
        }
    }

    /// <summary>
    /// Stops the scan named, when it is the one running. What it has read so far stays on record, and the next scan
    /// skips it; nothing is forgotten, as a scan forgets files gone from the folder only once it has gone through it all.
    /// </summary>
    /// <returns>Whether that scan was running.</returns>
    public bool Stop(string instanceId)
    {
        lock (_gate)
        {
            if (_current != instanceId) return false;
            _stop!.Cancel();
            return true;
        }
    }

    private async Task RunAsync(ScanRequest request, string instanceId, CancellationTokenSource stop)
    {
        try { await _scans.RunAsync(request, instanceId, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            _log.Log(instanceId, "Info", "Scan stopped. What was read so far is kept.");
        }
        // One of the catches of every error PhotoSense keeps (CLAUDE.md): the scan runs where nothing waits for it, so an
        // error it did not expect would go unseen, and the page would never learn why it stopped. The scan marks itself
        // finished whatever happens; what stopped it is told in its log.
        catch (Exception ex)
        {
            _log.Log(instanceId, "Error", $"The scan stopped: {ex.Message}");
        }
        finally
        {
            // Under the gate, so that Stop never cancels what has been let go of.
            lock (_gate) (_current, _stop) = (null, null);
            stop.Dispose();
        }
    }
}
