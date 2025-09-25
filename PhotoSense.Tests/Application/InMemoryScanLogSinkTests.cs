using Xunit;
using PhotoSense.Infrastructure.Scanning;
using System.Linq;

namespace PhotoSense.Tests.Application;

public class InMemoryScanLogSinkTests
{
    [Fact]
    public void RetainsRecent()
    {
        var sink = new InMemoryScanLogSink();
        for (int i=0;i<250;i++) sink.Log("a","Info","Msg "+i);
        var recent = sink.GetRecent("a");
        Assert.True(recent.Count <= 200);
    Assert.EndsWith("249", recent[recent.Count-1].message);
    }
}