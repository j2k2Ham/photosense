using PhotoSense.Application.Scanning.Events;

namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanRequestPublisher
{
    Task PublishAsync(ScanRequestedEvent evt, CancellationToken ct = default);
}

public sealed record ScanRequest(string PrimaryPath, string? SecondaryPath, bool Recursive);

/// <param name="Total">Pictures and videos found under the scanned folders.</param>
/// <param name="Analyzed">Files read and measured in this scan.</param>
/// <param name="Unchanged">Files skipped because they were unchanged since the last scan.</param>
/// <param name="Unreadable">Files that could not be opened, and pictures that could not be decoded.</param>
/// <param name="Pruned">Records dropped because their file is no longer part of the scanned folders.</param>
public sealed record ScanSummary(int Total, int Analyzed, int Unchanged, int Unreadable, int Pruned);

public interface IScanExecutionService
{
    /// <summary>
    /// Scans the folders and leaves the repository holding exactly one record per picture or video found.
    /// </summary>
    Task<ScanSummary> RunAsync(ScanRequest request, string instanceId, CancellationToken ct = default);
}
