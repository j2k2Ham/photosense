using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Entities;

public class Photo
{
    public PhotoId Id { get; init; } = PhotoId.New();
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public long FileSizeBytes { get; init; }
    // Last write time of the file when it was scanned; with the size it tells an unchanged file from a changed one.
    public DateTime? FileModifiedUtc { get; init; }
    // Which version of the scan wrote this record; an older record is read again instead of being trusted.
    public int AnalysisVersion { get; init; }
    // Folder the scan was started on; removed files are held beneath it.
    public string? ScanRoot { get; set; }
    public string? ContentHash { get; set; }
    // 64-bit DCT perceptual hash as 16 hex characters.
    public string? PerceptualHash { get; set; }
    // Small normalized copy of the picture, compared directly to confirm a perceptual-hash match.
    public byte[]? Signature { get; set; }
    // Pixel dimensions as displayed (orientation applied).
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Format { get; set; }
    // Encoder quality (1-100) when the format records one (JPEG).
    public int? EncodedQuality { get; set; }
    // Identifier an iPhone writes into both halves of a Live Photo: the picture and its short video.
    public string? LivePhotoId { get; set; }
    // Playing time, for videos.
    public double? DurationSeconds { get; set; }
    public DateTime? TakenOn { get; set; }
    public string? CameraModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public PhotoSet Set { get; set; }
    // User decision flags
    public bool IsKept { get; set; }
    public long PixelCount => (long)Width * Height;
    public bool IsVideo => MediaFiles.IsVideo(FileName);

    /// <summary>The same photo, recorded at the path its file was moved to.</summary>
    public Photo MovedTo(string newPath)
    {
        var moved = new Photo
        {
            Id = Id,
            SourcePath = newPath,
            FileName = Path.GetFileName(newPath),
            FileSizeBytes = FileSizeBytes,
            FileModifiedUtc = FileModifiedUtc,
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
            TakenOn = TakenOn,
            CameraModel = CameraModel,
            Latitude = Latitude,
            Longitude = Longitude,
            Set = Set,
            IsKept = IsKept
        };
        moved.LoadCategories(_categories);
        return moved;
    }

    public IReadOnlyCollection<string> Categories => _categories.AsReadOnly();
    private readonly List<string> _categories = new();
    public void AddCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category) && !_categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            _categories.Add(category);
    }
    public void LoadCategories(IEnumerable<string> categories)
    {
        _categories.Clear();
        _categories.AddRange(categories);
    }
}
