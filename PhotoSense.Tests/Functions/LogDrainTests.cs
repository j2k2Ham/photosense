using System.Reflection;
using PhotoSense.Functions.Realtime;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Tests.Functions;

// These classes log through one process-wide queue, so they must not run side by side.
[NotInParallel("ScanLogQueue")]
public class LogDrainTests
{
    [Test]
    public async Task DrainPending_Respects_Max()
    {
        var sink = new InMemoryScanLogSink();
        var method = typeof(SignalRLogFunctions).GetMethod("DrainPending", BindingFlags.NonPublic | BindingFlags.Static);
        await Assert.That(method).IsNotNull();
        // Ensure static queue is empty by draining with large max first.
        _ = ((IEnumerable<object>)method!.Invoke(null, new object[]{10000})!).ToList();

        int enqueued = 25;
        for (int i = 0; i < enqueued; i++) sink.Log("x", "Info", "M"+i);

        var first = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        await Assert.That(first.Count).IsEqualTo(10);
        var second = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        await Assert.That(second.Count).IsEqualTo(10);
        var third = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        await Assert.That(third.Count).IsEqualTo(enqueued - 20); // remaining
        // Ensure no extra items linger
        var fourth = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        await Assert.That(fourth).IsEmpty();
    }
}