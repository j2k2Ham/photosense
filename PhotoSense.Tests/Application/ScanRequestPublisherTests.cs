using PhotoSense.Application.Scanning.Events;
using PhotoSense.Application.Scanning.Services;
using Xunit;

namespace PhotoSense.Tests.Application;

public class ScanRequestPublisherTests
{
    [Fact]
    public async Task Publish_Then_Dequeue_Succeeds()
    {
        var publisher = new ScanRequestPublisher();
        var evt = new ScanRequestedEvent("inst","path", true);
        await publisher.PublishAsync(evt);
        var ok = ScanRequestPublisher.TryDequeue(out var dequeued);
        Assert.True(ok);
        Assert.Equal(evt.InstanceId, dequeued.InstanceId);
    }

    [Fact]
    public void Dequeue_Empty_ReturnsFalse()
    {
        var ok = ScanRequestPublisher.TryDequeue(out _);
        Assert.False(ok);
    }
}
