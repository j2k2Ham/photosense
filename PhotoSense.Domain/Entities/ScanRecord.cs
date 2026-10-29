namespace PhotoSense.Domain.Entities;

/// <summary>What one finished scan took, kept so that the next one can say how long it will be.</summary>
public class ScanRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime StartedUtc { get; init; }
    public double Seconds { get; init; }
    /// <summary>Files the scan covered.</summary>
    public int Total { get; init; }
    /// <summary>Files it had to open and read: new ones, changed ones, and those it could not make sense of.</summary>
    public int Read { get; init; }
    /// <summary>Files it skipped because nothing about them had changed since an earlier scan.</summary>
    public int Unchanged { get; init; }
}
