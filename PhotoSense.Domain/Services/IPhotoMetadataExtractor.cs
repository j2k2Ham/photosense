using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Services;

/// <summary>Reads what a file says about itself into the record a scan makes of it.</summary>
public interface IPhotoMetadataExtractor
{
    /// <summary>
    /// Fills in when and where the photo was taken, the camera, and the Live Photo it belongs to, as far as
    /// the file says. A file whose details cannot be read leaves the photo as it was.
    /// </summary>
    Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default);
}
