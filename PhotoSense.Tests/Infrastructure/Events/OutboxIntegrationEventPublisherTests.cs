using Moq;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Events;

namespace PhotoSense.Tests.Infrastructure.Events;

public class OutboxIntegrationEventPublisherTests
{
    [Test]
    public async Task Publish_Adds_To_Outbox()
    {
        var store = new Mock<IOutboxStore>();
        var publisher = new OutboxIntegrationEventPublisher(store.Object);
        var evt = new { Name = "Test" };
        await publisher.PublishAsync(evt);
        store.Verify(s => s.AddAsync(It.Is<OutboxMessage>(m => m.Type.Contains("AnonymousType") && m.Payload.Contains("Test")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task A_Message_Says_When_It_Was_Published()
    {
        var store = new Mock<IOutboxStore>();
        await new OutboxIntegrationEventPublisher(store.Object, new TestClock()).PublishAsync(new { Name = "Test" });
        store.Verify(s => s.AddAsync(It.Is<OutboxMessage>(m => m.OccurredUtc == TestClock.Day.UtcDateTime), It.IsAny<CancellationToken>()), Times.Once);
    }
}
