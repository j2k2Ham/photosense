using System.Reflection;
using PhotoSense.Functions.Realtime;
using PhotoSense.Infrastructure.Scanning;
using Xunit;

namespace PhotoSense.Tests.Functions;

public class LogDrainTests
{
    [Fact]
    public void DrainPending_Respects_Max()
    {
        var sink = new InMemoryScanLogSink();
        var method = typeof(SignalRLogFunctions).GetMethod("DrainPending", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        // Ensure static queue is empty by draining with large max first.
        _ = ((IEnumerable<object>)method!.Invoke(null, new object[]{10000})!).ToList();

        int enqueued = 25;
        for (int i = 0; i < enqueued; i++) sink.Log("x", "Info", "M"+i);

        var first = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        Assert.Equal(10, first.Count);
        var second = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        Assert.Equal(10, second.Count);
        var third = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        Assert.Equal(enqueued - 20, third.Count); // remaining
        // Ensure no extra items linger
        var fourth = ((IEnumerable<object>)method.Invoke(null, new object[]{10})!).ToList();
        Assert.Empty(fourth);
    }
}