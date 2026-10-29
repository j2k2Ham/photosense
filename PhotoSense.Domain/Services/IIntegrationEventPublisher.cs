namespace PhotoSense.Domain.Services;

/// <summary>Tells whatever listens outside the service that something happened, such as a photo being removed.</summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Hands the event on; it is kept until it has been sent.</summary>
    Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class;
}
