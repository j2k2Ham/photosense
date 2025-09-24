using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Services;

public interface IPhotoMetadataExtractor
{
    Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default);
}
