using PhotoSense.Application.Scanning.Events;
using PhotoSense.Application.Scanning.Services;

namespace PhotoSense.Tests.Application;

public class ScanRequestPublisherTests
{
    [Test]
    public async Task Publish_Then_Dequeue_Succeeds()
    {
        var publisher = new ScanRequestPublisher();
        var correlation = Guid.NewGuid();
        var evt = new ScanRequestedEvent(correlation, "p1", "p2", DateTime.UtcNow);
        await publisher.PublishAsync(evt);
        var ok = ScanRequestPublisher.TryDequeue(out var dequeued);
        await Assert.That(ok).IsTrue();
        await Assert.That(dequeued.CorrelationId).IsEqualTo(correlation);
    }

    [Test]
    public async Task Dequeue_Empty_ReturnsFalse()
    {
        var ok = ScanRequestPublisher.TryDequeue(out _);
        await Assert.That(ok).IsFalse();
    }
}
