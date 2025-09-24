using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoSense.Infrastructure.Metadata;

public class BasicExifMetadataExtractor : IPhotoMetadataExtractor
{
    public Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default)
    {
        try
        {
            imageStream.Position = 0;
            var directories = ImageMetadataReader.ReadMetadata(imageStream);
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (subIfd != null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                    photo.TakenOn = DateTime.SpecifyKind(dateTaken, DateTimeKind.Utc);
            }
            if (ifd0 != null)
            {
                photo.CameraModel = ifd0.GetDescription(ExifDirectoryBase.TagModel);
            }
            // GPS (simplified)
            var gps = directories.OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault();
            if (gps != null)
            {
                var loc = gps.GetGeoLocation();
                if (loc != null)
                {
                    photo.Latitude = loc.Latitude;
                    photo.Longitude = loc.Longitude;
                }
            }
        }
        catch { /* swallow - metadata optional */ }
        return Task.CompletedTask;
    }
}
