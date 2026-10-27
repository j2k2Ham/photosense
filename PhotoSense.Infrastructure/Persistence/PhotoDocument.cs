using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>A scan's record of one file as the database holds it.</summary>
internal sealed class PhotoDocument
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = default!;
    public string PathKey { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public long FileSizeBytes { get; set; }
    // Times are stored as ticks: LiteDB rounds DateTime to milliseconds and returns it as local time.
    public long? FileModifiedUtcTicks { get; set; }
    public int AnalysisVersion { get; set; }
    public string? ScanRoot { get; set; }
    public string? ContentHash { get; set; }
    public string? PerceptualHash { get; set; }
    public byte[]? Signature { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Format { get; set; }
    public int? EncodedQuality { get; set; }
    public string? LivePhotoId { get; set; }
    public double? DurationSeconds { get; set; }
    public long? TakenOnTicks { get; set; }
    public string? CameraModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Set { get; set; }
    public List<string> Categories { get; set; } = new();
    public bool IsKept { get; set; }

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
