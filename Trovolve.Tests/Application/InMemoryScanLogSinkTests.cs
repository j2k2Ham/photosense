using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trovolve.Application.Scanning;
using Trovolve.Application.Scanning.Interfaces;

namespace Trovolve.Tests.Application;

/// <summary>A scan's log lines, kept until the page asks for them.</summary>
public class InMemoryScanLogSinkTests
{
    [Test]
    public async Task Each_Line_Waits_For_The_Page_With_The_Scan_It_Belongs_To_And_When_It_Was_Written(CancellationToken ct)
    {
        var sink = new InMemoryScanLogSink(new TestClock(), NullLogger<InMemoryScanLogSink>.Instance);
        sink.Log("a", "Info", "first");
        sink.Log("a", "Warn", "second");
        sink.Log("b", "Info", "third");

        await Assert.That(sink.TakePending(2)).IsEquivalentTo([new ScanLogLine("a", TestClock.Day.UtcDateTime, "Info", "first"), new ScanLogLine("a", TestClock.Day.UtcDateTime, "Warn", "second")], CollectionOrdering.Matching);
        // Each line is handed over once.
        await Assert.That(sink.TakePending(10).Select(l => l.Message)).IsEquivalentTo(["third"], CollectionOrdering.Matching);
        await Assert.That(sink.TakePending(10)).IsEmpty();
    }

    [Test]
    public async Task Each_Line_Is_Also_Written_To_The_Programs_Log_At_The_Level_The_Page_Shows_It()
    {
        var log = new ListLogger<InMemoryScanLogSink>();
        var sink = new InMemoryScanLogSink(new TestClock(), log);
        sink.Log("scan-1", "Info", "Scanning 12 files");
        sink.Log("scan-1", "Warn", "Could not decode IMG_1.HEIC: it is empty.");
        sink.Log("audit", "Warning", "Put back a move cut short.");
        sink.Log("scan-1", "Error", "The scan stopped: the disk went away.");

        await Assert.That(log.Lines.Select(l => (l.Level, l.EventId, l.Message))).IsEquivalentTo([
            (LogLevel.Information, 1004, "scan-1: Scanning 12 files"),
            (LogLevel.Warning, 1004, "scan-1: Could not decode IMG_1.HEIC: it is empty."),
            (LogLevel.Warning, 1004, "audit: Put back a move cut short."),
            (LogLevel.Error, 1004, "scan-1: The scan stopped: the disk went away."),
        ], CollectionOrdering.Matching);
    }
}
