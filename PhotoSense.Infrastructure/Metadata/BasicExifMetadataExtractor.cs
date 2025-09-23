using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Services;

namespace PhotoSense.Infrastructure.Metadata;

public class BasicExifMetadataExtractor : IPhotoMetadataExtractor
{
    public Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default)
    {
        // Placeholder: later we can integrate a library like MetadataExtractor.
        // For now set nothing to keep it simple.
        return Task.CompletedTask;
    }
}
