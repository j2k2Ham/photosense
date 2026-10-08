using System.Globalization;
using System.Text.RegularExpressions;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using Directory = MetadataExtractor.Directory;

namespace PhotoSense.Infrastructure.Metadata;

public class BasicExifMetadataExtractor : IPhotoMetadataExtractor
{
    // QuickTime writes a position as signed degrees run together: +35.2886-075.5161+003.214/
    private static readonly Regex Iso6709 = new(@"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private const int QuickTimeCreationDate = 0x7, QuickTimeGpsLocation = 0xE, QuickTimeModel = 0x16;

    public Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct = default)
    {
        try
        {
            imageStream.Position = 0;
            Read(photo, ImageMetadataReader.ReadMetadata(imageStream));
        }
        catch { /* swallow - metadata optional */ }
        return Task.CompletedTask;
    }

    /// <summary>Records on the photo what the directories read from its file say about it.</summary>
    public static void Read(Photo photo, IReadOnlyList<Directory> directories)
    {
        photo.LivePhotoId = LivePhotoLink.ReadId(directories, photo.IsVideo);
        if (photo.IsVideo) ReadVideo(photo, directories);
        else ReadPicture(photo, directories);
    }

    private static void ReadPicture(Photo photo, IReadOnlyList<Directory> directories)
    {
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
        {
            // Capture time is wall-clock time where the picture was taken; it carries no zone.
            // Sub-seconds tell apart burst shots taken within the same second.
            var taken = DateTime.SpecifyKind(dateTaken, DateTimeKind.Unspecified);
            var subSeconds = subIfd.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal)?.Trim();
            if (!string.IsNullOrEmpty(subSeconds) && subSeconds.All(char.IsAsciiDigit) && taken.Millisecond == 0)
            {
                // The digits are a decimal fraction of a second; a tick is one ten-millionth.
                var fraction = subSeconds.Length > 7 ? subSeconds[..7] : subSeconds.PadRight(7, '0');
                taken = taken.AddTicks(long.Parse(fraction, CultureInfo.InvariantCulture));
            }
            photo.TakenOn = taken;
        }
        if (ifd0 != null)
        {
            photo.CameraModel = ifd0.GetDescription(ExifDirectoryBase.TagModel);
        }
        var loc = directories.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
        // 0,0 is what devices write when they have no fix.
        if (loc != null && !loc.IsZero)
        {
            photo.Latitude = loc.Latitude;
            photo.Longitude = loc.Longitude;
        }
    }

    private static void ReadVideo(Photo photo, IReadOnlyList<Directory> directories)
    {
        var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (movie?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration && duration > TimeSpan.Zero)
            photo.DurationSeconds = duration.TotalSeconds;

        foreach (var track in directories.OfType<QuickTimeTrackHeaderDirectory>())
        {
            // The picture track is the one with a size; sound and data tracks report none.
            if (!track.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var width) || width <= 0) continue;
            if (!track.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var height) || height <= 0) continue;
            // A phone held upright records sideways and notes the turn.
            var sideways = track.TryGetDouble(QuickTimeTrackHeaderDirectory.TagRotation, out var rotation) && Math.Abs(Math.Abs(rotation) % 180 - 90) < 1;
            (photo.Width, photo.Height) = sideways ? (height, width) : (width, height);
            break;
        }

        var meta = directories.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault();
        photo.CameraModel = meta?.GetString(QuickTimeModel);
        if (meta?.GetString(QuickTimeGpsLocation) is { } position && Iso6709.Match(position) is var m && m.Success)
        {
            var latitude = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var longitude = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (latitude != 0 || longitude != 0) (photo.Latitude, photo.Longitude) = (latitude, longitude);
        }

        // The phone's own creation date is local time; the container's is UTC and reads 1904 when never set.
        if (meta != null && meta.TryGetDateTime(QuickTimeCreationDate, out var created))
            photo.TakenOn = DateTime.SpecifyKind(created.Kind == DateTimeKind.Utc ? created.ToLocalTime() : created, DateTimeKind.Unspecified);
        else if (movie != null && movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var written) && written.Year > 1970)
            photo.TakenOn = DateTime.SpecifyKind(DateTime.SpecifyKind(written, DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);
    }
}
