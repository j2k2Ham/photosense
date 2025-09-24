using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Core.Domain.Entities;

public class Photo
{
    public PhotoId Id { get; init; } = PhotoId.New();
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public long FileSizeBytes { get; init; }
    public string? ContentHash { get; set; }
    public string? PerceptualHash { get; set; } // aHash/dHash for near-duplicate detection
    public DateTime? TakenOn { get; set; }
    public string? CameraModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public PhotoSet Set { get; init; }
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
