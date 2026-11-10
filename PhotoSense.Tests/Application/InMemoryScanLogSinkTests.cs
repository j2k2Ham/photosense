using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Tests.Application;

/// <summary>A scan's log lines, kept until the page asks for them.</summary>
public class InMemoryScanLogSinkTests
{
    [Test]
    public async Task Each_Line_Waits_For_The_Page_With_The_Scan_It_Belongs_To_And_When_It_Was_Written(CancellationToken ct)
    {
        var sink = new InMemoryScanLogSink(new TestClock());
        sink.Log("a", "Info", "first");
        sink.Log("a", "Warn", "second");
        sink.Log("b", "Info", "third");

        await Assert.That(sink.TakePending(2)).IsEquivalentTo([new ScanLogLine("a", TestClock.Day.UtcDateTime, "Info", "first"), new ScanLogLine("a", TestClock.Day.UtcDateTime, "Warn", "second")], CollectionOrdering.Matching);
        // Each line is handed over once.
        await Assert.That(sink.TakePending(10).Select(l => l.Message)).IsEquivalentTo(["third"], CollectionOrdering.Matching);
        await Assert.That(sink.TakePending(10)).IsEmpty();
    }
}
