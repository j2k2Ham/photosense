using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Api.Scanning;

/// <summary>
/// Runs a scan in the background of whichever program serves the endpoints, one scan at a time. What it has
/// got to is kept in the progress store, which the status and progress addresses read.
/// </summary>
public sealed class ScanRunner
{
    private readonly IScanExecutionService _scans;
    private readonly IScanProgressStore _progress;
    private readonly IScanLogSink _log;
    // Requests are taken one at a time, so a second click cannot slip in before the first scan is marked as started.
    private readonly object _gate = new();

    public ScanRunner(IScanExecutionService scans, IScanProgressStore progress, IScanLogSink log)
    {
        _scans = scans;
        _progress = progress;
        _log = log;
    }

    /// <summary>The scan last started, while it runs; a finished one stays here to be waited for.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

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
            Running = Task.Run(() => RunAsync(request, instanceId));
            return instanceId;
        }
    }

    private async Task RunAsync(ScanRequest request, string instanceId)
    {
        try { await _scans.RunAsync(request, instanceId); }
        catch (Exception ex)
        {
            // The scan marks itself finished whatever happens; what stopped it is told in its log.
            _log.Log(instanceId, "Error", $"The scan stopped: {ex.Message}");
        }
    }
}
