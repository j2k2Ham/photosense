namespace PhotoSense.Domain.Services;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class;
}
