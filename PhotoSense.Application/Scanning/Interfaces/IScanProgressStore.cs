namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>How far each scan has got, for the page to show while it runs.</summary>
public interface IScanProgressStore
{
    /// <summary>Notes that a scan has begun; it becomes the latest.</summary>
    void ScanStarted(string instanceId);
    /// <summary>Records how many files each folder of the scan holds.</summary>
    void SetTotals(string instanceId, int primaryTotal, int secondaryTotal);
    /// <summary>Records how long the scan is expected to take in all, going by earlier scans; null when they say nothing.</summary>
    void Expect(string instanceId, double? seconds);
    /// <summary>Counts one more file gone through, in the first folder or the second.</summary>
    void IncrementProcessed(string instanceId, bool primary);
    /// <summary>Notes that the scan has ended, however it ended.</summary>
    void ScanCompleted(string instanceId);
    /// <summary>How far a scan has got; one with nothing done when the scan is not known.</summary>
    ScanProgressSnapshot Get(string instanceId);
    /// <summary>How far the latest scan has got; one with no id when there has been none.</summary>
    ScanProgressSnapshot GetLatest();
}

/// <summary>How far a scan had got when asked.</summary>
/// <param name="InstanceId">The scan.</param>
/// <param name="StartedUtc">When it began.</param>
/// <param name="CompletedUtc">When it ended; null while it runs.</param>
/// <param name="PrimaryTotal">Files in the first folder.</param>
/// <param name="PrimaryProcessed">Of those, how many have been gone through.</param>
/// <param name="SecondaryTotal">Files in the second folder.</param>
/// <param name="SecondaryProcessed">Of those, how many have been gone through.</param>
public sealed record ScanProgressSnapshot(
    string InstanceId,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    int PrimaryTotal,
    int PrimaryProcessed,
    int SecondaryTotal,
    int SecondaryProcessed)
{
    /// <summary>How long earlier scans suggest this one will take in all, when they have anything to say.</summary>
    public double? ExpectedSeconds { get; init; }

    /// <summary>How much of the first folder has been gone through, out of 100; 100 for an empty one.</summary>
    public double PrimaryPercent => PrimaryTotal == 0 ? 100 : (double)PrimaryProcessed / PrimaryTotal * 100d;
    /// <summary>How much of the second folder has been gone through, out of 100; 100 for an empty one or none.</summary>
    public double SecondaryPercent => SecondaryTotal == 0 ? 100 : (double)SecondaryProcessed / SecondaryTotal * 100d;
    /// <summary>How much of the whole scan has been gone through, out of 100; 100 when there was nothing to go through.</summary>
    public double OverallPercent
    {
        get
        {
            var total = PrimaryTotal + SecondaryTotal;
            var done = PrimaryProcessed + SecondaryProcessed;
            return total == 0 ? 100 : (double)done / total * 100d;
        }
    }
}
