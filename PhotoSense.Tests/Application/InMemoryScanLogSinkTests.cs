using PhotoSense.Application.Scanning;

namespace PhotoSense.Tests.Application;

// These classes log through one process-wide queue, so they must not run side by side.
/// <summary>A scan's log lines, kept until the page asks for them.</summary>
[NotInParallel("ScanLogQueue")]
public class InMemoryScanLogSinkTests
{
    [Test]
    public async Task Each_Line_Waits_For_The_Page_With_The_Scan_It_Belongs_To_And_When_It_Was_Written(CancellationToken ct)
    {
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
        var sink = new InMemoryScanLogSink(new TestClock());
        sink.Log("a", "Info", "first");

        await Assert.That(InMemoryScanLogSink.TryDequeuePending(out var sent)).IsTrue();
        await Assert.That(sent).IsEqualTo(("a", TestClock.Day.UtcDateTime, "Info", "first"));
    }
}
