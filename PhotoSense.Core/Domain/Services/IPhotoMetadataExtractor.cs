using PhotoSense.Core.Domain.Entities;

namespace PhotoSense.Core.Domain.Services;

public interface IPhotoMetadataExtractor
{
    Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default);
}
