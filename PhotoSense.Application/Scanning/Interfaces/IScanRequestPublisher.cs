using PhotoSense.Application.Scanning.Events;
using PhotoSense.Core.Domain.Entities;

namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanRequestPublisher
{
    Task PublishAsync(ScanRequestedEvent evt, CancellationToken ct = default);
}

public interface IPhotoIngestionService
{
    Task<bool> IngestAsync(string filePath, PhotoSet set, CancellationToken ct = default);
}

public interface IScanExecutionService
{
    Task<int> CountAsync(string path, bool recursive);
    Task ProcessAsync(IEnumerable<string> files, PhotoSet set, string instanceId, CancellationToken ct = default);
}
