using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Scanning;

/// <summary>How long a scan has left to run.</summary>
public static class ScanEstimate
{
    /// <summary>A scan that has read fewer files than this says little about how long a file takes.</summary>
    public const int EnoughFiles = 50;

    /// <summary>A scan's own pace is gone by once it has run this long, and has that many files behind it.</summary>
    public const double EnoughSeconds = 10;

    private const int ScansConsulted = 3;

    /// <summary>
    /// Seconds one file took to read in the latest scans that read enough files to tell, or null when none
    /// has. Files a scan skipped as unchanged cost next to nothing and are not counted.
    /// </summary>
    public static double? SecondsPerFileRead(IEnumerable<ScanRecord> newestFirst)
    {
        var telling = newestFirst.Where(s => s.Read >= EnoughFiles).Take(ScansConsulted).ToList();
        return telling.Count == 0 ? null : telling.Sum(s => s.Seconds) / telling.Sum(s => s.Read);
    }

    /// <summary>Seconds left, or null while there is nothing yet to go by.</summary>
    public static double? SecondsLeft(ScanProgressSnapshot scan, DateTime nowUtc)
    {
        if (scan.CompletedUtc is not null) return 0;
        var total = scan.PrimaryTotal + scan.SecondaryTotal;
        var done = scan.PrimaryProcessed + scan.SecondaryProcessed;
        var elapsed = (nowUtc - scan.StartedUtc).TotalSeconds;

        // The scan's own pace, once there is enough of it: it takes in how many files turn out to need reading.
        if (done >= EnoughFiles && elapsed >= EnoughSeconds) return (total - done) * elapsed / done;

        // Until then, what earlier scans took for as many files as this one has not seen before.
        return scan.ExpectedSeconds is { } expected ? Math.Max(0, expected - elapsed) : null;
    }
}
