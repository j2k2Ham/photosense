using Xunit;
using PhotoSense.Infrastructure.Scanning;
using System.Linq;

namespace PhotoSense.Tests.Functions;

public class LogsFilterTests
{
    [Fact]
    public async Task Since_Filter_Works()
    {
        var sink = new InMemoryScanLogSink();
        sink.Log("x","Info","A");
        var mid = DateTime.UtcNow;
        await Task.Delay(10);
        sink.Log("x","Info","B");
        var recent = sink.GetRecent("x").Where(l=>l.ts>mid).ToList();
        Assert.Single(recent);
        Assert.Equal("B", recent[0].message);
    }
}