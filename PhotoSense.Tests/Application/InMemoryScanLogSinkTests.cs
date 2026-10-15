using PhotoSense.Infrastructure.Scanning;
using System.Linq;

namespace PhotoSense.Tests.Application;

// These classes log through one process-wide queue, so they must not run side by side.
[NotInParallel("ScanLogQueue")]
public class InMemoryScanLogSinkTests
{
    [Test]
    public async Task RetainsRecent()
    {
        var sink = new InMemoryScanLogSink();
        for (int i=0;i<250;i++) sink.Log("a","Info","Msg "+i);
        var recent = sink.GetRecent("a");
        await Assert.That(recent.Count <= 200).IsTrue();
    await Assert.That(recent[recent.Count-1].message).EndsWith("249");
    }

    [Test]
    public async Task Each_Line_Says_When_It_Was_Written_Both_Here_And_In_What_Is_Sent_On()
    {
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
        var sink = new InMemoryScanLogSink(new TestClock());
        sink.Log("a", "Info", "first");

        await Assert.That(sink.GetRecent("a").Single().ts).IsEqualTo(TestClock.Day.UtcDateTime);
        await Assert.That(InMemoryScanLogSink.TryDequeuePending(out var sent)).IsTrue();
        await Assert.That(sent.ts).IsEqualTo(TestClock.Day.UtcDateTime);
    }
}