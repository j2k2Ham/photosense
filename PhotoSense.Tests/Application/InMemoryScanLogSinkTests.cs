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
}