using PhotoSense.Infrastructure.Scanning;
using System.Linq;

namespace PhotoSense.Tests.Functions;

// These classes log through one process-wide queue, so they must not run side by side.
[NotInParallel("ScanLogQueue")]
public class LogsFilterTests
{
    [Test]
    public async Task Since_Filter_Works()
    {
        var sink = new InMemoryScanLogSink();
    sink.Log("x","Info","A");
        var mid = DateTime.UtcNow;
        await Task.Delay(10);
        sink.Log("x","Info","B");
        var recent = sink.GetRecent("x").Where(l=>l.ts>mid).ToList();
        await Assert.That(recent).HasSingleItem();
        await Assert.That(recent[0].message).IsEqualTo("B");
    }
}