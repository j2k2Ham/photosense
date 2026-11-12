using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>A scan's record of one file as the database holds it.</summary>
internal sealed class PhotoDocument
{
    /// <summary>The record's id.</summary>
    public Guid Id { get; set; }
    /// <summary>The file's full path.</summary>
    public string SourcePath { get; set; } = default!;
    /// <summary>The key two spellings of the path share, by which a file has one record.</summary>
    public string PathKey { get; set; } = default!;
    /// <summary>The file's name.</summary>
    public string FileName { get; set; } = default!;
    /// <summary>The file's size when it was scanned.</summary>
    public long FileSizeBytes { get; set; }
    // Times are stored as ticks: LiteDB rounds DateTime to milliseconds and returns it as local time.
    /// <summary>When the file had last changed when it was scanned.</summary>
    public long? FileModifiedUtcTicks { get; set; }
    /// <summary>Which version of the scan wrote the record.</summary>
    public int AnalysisVersion { get; set; }
    /// <summary>The folder the scan was started on.</summary>
    public string? ScanRoot { get; set; }
    /// <summary>The hash of the file's bytes.</summary>
    public string? ContentHash { get; set; }
    /// <summary>The perceptual hash, as 16 hex characters.</summary>
    public string? PerceptualHash { get; set; }
    /// <summary>The picture reduced to a small grid of colours.</summary>
    public byte[]? Signature { get; set; }
    /// <summary>Width in pixels as displayed.</summary>
    public int Width { get; set; }
    /// <summary>Height in pixels as displayed.</summary>
    public int Height { get; set; }
    /// <summary>The format as the decoder named it.</summary>
    public string? Format { get; set; }
    /// <summary>JPEG quality, where the format records one.</summary>
    public int? EncodedQuality { get; set; }
    /// <summary>The Live Photo identifier it carries.</summary>
    public string? LivePhotoId { get; set; }
    /// <summary>Playing time, for a video.</summary>
    public double? DurationSeconds { get; set; }
    /// <summary>When it was taken, as wall-clock ticks with no zone.</summary>
    public long? TakenOnTicks { get; set; }
    /// <summary>The camera or phone that took it.</summary>
    public string? CameraModel { get; set; }
    /// <summary>Where it was taken: degrees north.</summary>
    public double? Latitude { get; set; }
    /// <summary>Where it was taken: degrees east.</summary>
    public double? Longitude { get; set; }
    /// <summary>Which of the scanned folders it is in.</summary>
    public int Set { get; set; }
    /// <summary>Words that describe it.</summary>
    public List<string> Categories { get; set; } = new();
    /// <summary>The person marked it to keep.</summary>
    public bool IsKept { get; set; }

    /// <summary>The record as the database holds it.</summary>
    public static PhotoDocument From(Photo p) => new()
    {
        Id = p.Id.Value,
        SourcePath = p.SourcePath,
        PathKey = PhotoPath.Key(p.SourcePath),
        FileName = p.FileName,
        FileSizeBytes = p.FileSizeBytes,
        FileModifiedUtcTicks = p.FileModifiedUtc?.Ticks,
        AnalysisVersion = p.AnalysisVersion,
        ScanRoot = p.ScanRoot,
        ContentHash = p.ContentHash,
        PerceptualHash = p.PerceptualHash,
        Signature = p.Signature,
        Width = p.Width,
        Height = p.Height,
        Format = p.Format,
        EncodedQuality = p.EncodedQuality,
        LivePhotoId = p.LivePhotoId,
        DurationSeconds = p.DurationSeconds,
        TakenOnTicks = p.TakenOn?.Ticks,
        CameraModel = p.CameraModel,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        Set = (int)p.Set,
        Categories = p.Categories.ToList(),
        IsKept = p.IsKept
    };

    /// <summary>The record as the rest of PhotoSense knows it.</summary>
    public Photo ToEntity()
    {
        var entity = new Photo
        {
            Id = new PhotoId(Id),
            SourcePath = SourcePath,
            FileName = FileName,
            FileSizeBytes = FileSizeBytes,
            FileModifiedUtc = FileModifiedUtcTicks is { } m ? new DateTime(m, DateTimeKind.Utc) : null,
            AnalysisVersion = AnalysisVersion,
            ScanRoot = ScanRoot,
            ContentHash = ContentHash,
            PerceptualHash = PerceptualHash,
            Signature = Signature,
            Width = Width,
            Height = Height,
            Format = Format,
            EncodedQuality = EncodedQuality,
            LivePhotoId = LivePhotoId,
            DurationSeconds = DurationSeconds,
            // Capture time is the wall-clock time where the picture was taken, so it carries no zone.
            TakenOn = TakenOnTicks is { } t ? new DateTime(t, DateTimeKind.Unspecified) : null,
            CameraModel = CameraModel,
            Latitude = Latitude,
            Longitude = Longitude,
            Set = (PhotoSet)Set,
            IsKept = IsKept
        };
        entity.LoadCategories(Categories);
        return entity;
    }
}
