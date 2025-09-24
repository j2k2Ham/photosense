namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanProgressStore
{
    void ScanStarted(string instanceId);
    void SetTotals(string instanceId, int primaryTotal, int secondaryTotal);
    void IncrementProcessed(string instanceId, bool primary);
    void ScanCompleted(string instanceId);
    ScanProgressSnapshot Get(string instanceId);
    ScanProgressSnapshot GetLatest(); // convenience
}

public sealed record ScanProgressSnapshot(
    string InstanceId,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    int PrimaryTotal,
    int PrimaryProcessed,
    int SecondaryTotal,
    int SecondaryProcessed)
{
    public double PrimaryPercent => PrimaryTotal == 0 ? 100 : (double)PrimaryProcessed / PrimaryTotal * 100d;
    public double SecondaryPercent => SecondaryTotal == 0 ? 100 : (double)SecondaryProcessed / SecondaryTotal * 100d;
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