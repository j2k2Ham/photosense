using Moq;
using PhotoSense.Core.Domain.Events;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Infrastructure.Events;
using Xunit;

namespace PhotoSense.Tests.Infrastructure.Events;

public class OutboxIntegrationEventPublisherTests
{
    [Fact]
    public async Task Publish_Adds_To_Outbox()
    {
        var store = new Mock<IOutboxStore>();
        var publisher = new OutboxIntegrationEventPublisher(store.Object);
        var evt = new { Name = "Test" };
        await publisher.PublishAsync(evt);
        store.Verify(s => s.AddAsync(It.Is<OutboxMessage>(m => m.Type.Contains("AnonymousType") && m.Payload.Contains("Test")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
