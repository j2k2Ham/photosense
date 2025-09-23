using PhotoSense.Application.Scanning.Events;

namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanRequestPublisher
{
    Task PublishAsync(ScanRequestedEvent evt, CancellationToken ct = default);
}
