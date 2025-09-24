using PhotoSense.Application.Scanning.Interfaces;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class ScanProgressSnapshotTests
{
    [Fact]
    public void Percentages_All_Zero_Totals_Are_100()
    {
        var snap = new ScanProgressSnapshot("id", DateTime.UtcNow, null, 0, 0, 0, 0);
        Assert.Equal(100, snap.PrimaryPercent);
        Assert.Equal(100, snap.SecondaryPercent);
        Assert.Equal(100, snap.OverallPercent);
    }

    [Fact]
    public void OverallPercent_Computed()
    {
        var snap = new ScanProgressSnapshot("id", DateTime.UtcNow, null, 10, 5, 10, 10);
        Assert.Equal(50, (int)snap.PrimaryPercent);
        Assert.Equal(100, (int)snap.SecondaryPercent);
        Assert.InRange(snap.OverallPercent, 74.9, 75.1); // 15/20 = 75%
    }
}
