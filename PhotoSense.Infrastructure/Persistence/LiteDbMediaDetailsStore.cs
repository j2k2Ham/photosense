using LiteDB;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbMediaDetailsStore : IMediaDetailsStore
{
    private readonly ILiteCollection<Kept> _col;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbMediaDetailsStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<Kept>();
        _col = db.GetCollection<Kept>("media_details");
    }

    public StoredMediaDetails? At(string path) => _col.FindById(PhotoPath.Key(path))?.ToStored();

    public IReadOnlyList<StoredMediaDetails> Under(string folder)
    {
        // Every path inside the folder begins with the folder and a separator; a folder beside it whose name
        // merely begins the same way does not.
        var prefix = PhotoPath.Key(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _col.Find(Query.StartsWith("_id", prefix)).Select(k => k.ToStored()).ToList();
    }

    public void Save(IEnumerable<StoredMediaDetails> details) => _col.Upsert(details.Select(Kept.From));

    // One file, under the key that two spellings of its path share.
    private sealed class Kept
    {
        public string Id { get; set; } = default!;
        public string Path { get; set; } = default!;
        public long SizeBytes { get; set; }
        // Times are stored as ticks: LiteDB rounds DateTime to milliseconds and returns it as local time.
        public long ModifiedUtcTicks { get; set; }
        public long? TakenOnTicks { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double? DurationSeconds { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? LivePhotoId { get; set; }

        public static Kept From(StoredMediaDetails s) => new()
        {
            Id = PhotoPath.Key(s.Path), Path = s.Path, SizeBytes = s.SizeBytes, ModifiedUtcTicks = s.ModifiedUtc.Ticks, TakenOnTicks = s.Details.TakenOn?.Ticks,
            Width = s.Details.Width, Height = s.Details.Height, DurationSeconds = s.Details.DurationSeconds, Latitude = s.Details.Latitude, Longitude = s.Details.Longitude, LivePhotoId = s.Details.LivePhotoId,
        };

        // Capture time is the wall-clock time where the picture was taken, so it carries no zone.
        public StoredMediaDetails ToStored() => new(Path, SizeBytes, new DateTime(ModifiedUtcTicks, DateTimeKind.Utc),
            new MediaDetails(TakenOnTicks is { } taken ? new DateTime(taken, DateTimeKind.Unspecified) : null, Width, Height, DurationSeconds, Latitude, Longitude, LivePhotoId));
    }
}
