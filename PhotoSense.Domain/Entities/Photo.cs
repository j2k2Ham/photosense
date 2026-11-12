using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Entities;

/// <summary>What a scan recorded about one picture or video: where it is, what it is, and what the person decided about it.</summary>
public class Photo
{
    /// <summary>Tells this record from every other; it stays the same when the file is moved.</summary>
    public PhotoId Id { get; init; } = PhotoId.New();
    /// <summary>The file's full path.</summary>
    public required string SourcePath { get; init; }
    /// <summary>The file's name, extension included.</summary>
    public required string FileName { get; init; }
    /// <summary>The file's size when it was scanned.</summary>
    public long FileSizeBytes { get; init; }
    /// <summary>Last write time of the file when it was scanned; with the size it tells an unchanged file from a changed one.</summary>
    public DateTime? FileModifiedUtc { get; init; }
    /// <summary>Which version of the scan wrote this record; an older record is read again instead of being trusted.</summary>
    public int AnalysisVersion { get; init; }
    /// <summary>Folder the scan was started on; removed files are held beneath it.</summary>
    public string? ScanRoot { get; set; }
    /// <summary>Hash of the file's bytes: files with the same one are identical.</summary>
    public string? ContentHash { get; set; }
    /// <summary>64-bit DCT perceptual hash as 16 hex characters.</summary>
    public string? PerceptualHash { get; set; }
    /// <summary>Small normalized copy of the picture, compared directly to confirm a perceptual-hash match.</summary>
    public byte[]? Signature { get; set; }
    /// <summary>Width in pixels as displayed (orientation applied).</summary>
    public int Width { get; set; }
    /// <summary>Height in pixels as displayed (orientation applied).</summary>
    public int Height { get; set; }
    /// <summary>The picture's format as its decoder names it ("JPEG", "HEIC"); null for a video or a file that could not be decoded.</summary>
    public string? Format { get; set; }
    /// <summary>Encoder quality (1-100) when the format records one (JPEG).</summary>
    public int? EncodedQuality { get; set; }
    /// <summary>Identifier an iPhone writes into both halves of a Live Photo: the picture and its short video.</summary>
    public string? LivePhotoId { get; set; }
    /// <summary>Playing time, for videos.</summary>
    public double? DurationSeconds { get; set; }
    /// <summary>When the picture or video was taken, as the file records it.</summary>
    public DateTime? TakenOn { get; set; }
    /// <summary>The camera or phone that took it, as the file records it.</summary>
    public string? CameraModel { get; set; }
    /// <summary>Where it was taken: degrees north, negative for south.</summary>
    public double? Latitude { get; set; }
    /// <summary>Where it was taken: degrees east, negative for west.</summary>
    public double? Longitude { get; set; }
    /// <summary>Which of the scanned folders the file is in.</summary>
    public PhotoSet Set { get; set; }
    /// <summary>The person marked it to keep, so no bulk removal takes it.</summary>
    public bool IsKept { get; set; }
    /// <summary>Width times height: the larger copy of a shot is the better one to keep.</summary>
    public long PixelCount => (long)Width * Height;
    /// <summary>Whether the file is a video, by its extension.</summary>
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

    /// <summary>Words that describe the photo, each once whatever its case.</summary>
    public IReadOnlyCollection<string> Categories => _categories.AsReadOnly();
    private readonly List<string> _categories = new();

    /// <summary>Adds a word that describes the photo, unless it is blank or already there.</summary>
    public void AddCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category) && !_categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            _categories.Add(category);
    }

    /// <summary>Puts back the words a stored record had, in place of any it has now.</summary>
    public void LoadCategories(IEnumerable<string> categories)
    {
        _categories.Clear();
        _categories.AddRange(categories);
    }
}
