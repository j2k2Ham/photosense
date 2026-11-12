namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>What to scan.</summary>
/// <param name="PrimaryPath">The first folder.</param>
/// <param name="SecondaryPath">A second folder, scanned with the first; null or blank for none.</param>
/// <param name="Recursive">Scan the folders inside them too.</param>
/// <param name="StartOver">Forget what earlier scans recorded and read every file again, instead of skipping files that have not changed.</param>
public sealed record ScanRequest(string PrimaryPath, string? SecondaryPath, bool Recursive, bool StartOver = false);

/// <summary>What one scan did.</summary>
/// <param name="Total">Pictures and videos found under the scanned folders.</param>
/// <param name="Analyzed">Files read and measured in this scan.</param>
/// <param name="Unchanged">Files skipped because they were unchanged since the last scan.</param>
/// <param name="Unreadable">Files that could not be opened, and pictures that could not be decoded.</param>
/// <param name="Pruned">Records dropped because their file is no longer part of the scanned folders.</param>
public sealed record ScanSummary(int Total, int Analyzed, int Unchanged, int Unreadable, int Pruned);

/// <summary>Scans folders for pictures and videos, and records what each is.</summary>
public interface IScanExecutionService
{
    /// <summary>
    /// Scans the folders and leaves the repository holding exactly one record per picture or video found.
    /// </summary>
    /// <param name="request">What to scan.</param>
    /// <param name="instanceId">Names this scan in its progress and its log.</param>
    /// <param name="ct">Stops the scan between files.</param>
    Task<ScanSummary> RunAsync(ScanRequest request, string instanceId, CancellationToken ct);
}
