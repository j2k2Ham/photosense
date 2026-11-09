using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Tests.Domain;

/// <summary>How far a scan has got, as a share of its files.</summary>
public class ScanProgressSnapshotTests
{
    [Test]
    public async Task Percentages_All_Zero_Totals_Are_100(CancellationToken ct)
    {
        var snap = new ScanProgressSnapshot("id", TestClock.Day.UtcDateTime, null, 0, 0, 0, 0);
        await Assert.That(snap.PrimaryPercent).IsEqualTo(100);
        await Assert.That(snap.SecondaryPercent).IsEqualTo(100);
        await Assert.That(snap.OverallPercent).IsEqualTo(100);
    }

    [Test]
    public async Task OverallPercent_Computed(CancellationToken ct)
    {
        var snap = new ScanProgressSnapshot("id", TestClock.Day.UtcDateTime, null, 10, 5, 10, 10);
        await Assert.That((int)snap.PrimaryPercent).IsEqualTo(50);
        await Assert.That((int)snap.SecondaryPercent).IsEqualTo(100);
        await Assert.That(snap.OverallPercent).IsGreaterThanOrEqualTo(74.9).And.IsLessThanOrEqualTo(75.1); // 15/20 = 75%
    }
}
