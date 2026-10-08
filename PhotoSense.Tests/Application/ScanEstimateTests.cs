using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Application;

public class ScanEstimateTests
{
    private static readonly DateTime Started = new(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);

    private static ScanRecord Scan(double seconds, int read, int unchanged = 0) => new() { Seconds = seconds, Read = read, Unchanged = unchanged, Total = read + unchanged };

    private static ScanProgressSnapshot Running(int total, int done, double? expected = null, int secondaryTotal = 0, int secondaryDone = 0)
        => new("scan", Started, null, total, done, secondaryTotal, secondaryDone) { ExpectedSeconds = expected };

    [Test]
    public async Task With_No_Scan_Behind_It_There_Is_No_Pace_To_Go_By()
        => await Assert.That(ScanEstimate.SecondsPerFileRead([])).IsNull();

    [Test]
    public async Task A_Scan_That_Read_Too_Few_Files_Says_Nothing_About_Pace()
    {
        // A second scan of a library skips nearly everything: it finished in seconds and read three files.
        await Assert.That(ScanEstimate.SecondsPerFileRead([Scan(4, read: 3, unchanged: 6_937), Scan(1, read: ScanEstimate.EnoughFiles - 1)])).IsNull();
        await Assert.That(ScanEstimate.SecondsPerFileRead([Scan(4, read: 3, unchanged: 6_937), Scan(780, read: 6_940)])).IsEqualTo(780d / 6_940);
    }

    [Test]
    public async Task Pace_Is_What_The_Three_Latest_Telling_Scans_Took_For_A_File_Between_Them()
    {
        var newestFirst = new[] { Scan(100, read: 50), Scan(2, read: 1), Scan(300, read: 100), Scan(200, read: 50), Scan(9_000, read: 50) };
        await Assert.That(ScanEstimate.SecondsPerFileRead(newestFirst)).IsEqualTo(600d / 200);
    }

    [Test]
    public async Task A_Finished_Scan_Has_Nothing_Left()
        => await Assert.That(ScanEstimate.SecondsLeft(new("scan", Started, Started.AddMinutes(13), 100, 100, 0, 0) { ExpectedSeconds = 500 }, Started.AddHours(1))).IsEqualTo(0);

    [Test]
    public async Task Once_It_Has_Enough_Behind_It_A_Scan_Goes_By_Its_Own_Pace()
    {
        // 200 of 1,000 files in 100 seconds, across both folders: the other 800 at the same pace.
        var scan = Running(total: 600, done: 150, expected: 9_999, secondaryTotal: 400, secondaryDone: 50);
        await Assert.That(ScanEstimate.SecondsLeft(scan, Started.AddSeconds(100))).IsEqualTo(400);
    }

    [Test]
    [Arguments(ScanEstimate.EnoughFiles - 1, 60)]   // too few files yet
    [Arguments(500, 9)]                             // too little time yet
    public async Task Until_Then_It_Goes_By_What_Earlier_Scans_Took(int done, int elapsed)
    {
        await Assert.That(ScanEstimate.SecondsLeft(Running(1_000, done, expected: 780), Started.AddSeconds(elapsed))).IsEqualTo(780 - elapsed);
        // With no earlier scan to go by, nothing can be said yet.
        await Assert.That(ScanEstimate.SecondsLeft(Running(1_000, done), Started.AddSeconds(elapsed))).IsNull();
    }

    [Test]
    public async Task A_Scan_That_Outlasts_What_Was_Expected_Has_No_Less_Than_Nothing_Left()
        => await Assert.That(ScanEstimate.SecondsLeft(Running(1_000, 10, expected: 5), Started.AddSeconds(8))).IsEqualTo(0);

    [Test]
    public async Task Nothing_Is_Said_Before_The_Files_Are_Counted()
        => await Assert.That(ScanEstimate.SecondsLeft(Running(0, 0), Started.AddSeconds(30))).IsNull();
}
