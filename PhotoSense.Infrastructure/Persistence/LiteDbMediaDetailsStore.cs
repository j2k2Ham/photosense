using LiteDB;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>What Organize read from files, kept in the database so that a folder is read once and not at every start.</summary>
internal sealed class LiteDbMediaDetailsStore : IMediaDetailsStore
{
    private readonly ILiteCollection<Kept> _col;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbMediaDetailsStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<Kept>();
        _col = db.GetCollection<Kept>("media_details");
    }

    /// <inheritdoc />
    public StoredMediaDetails? At(string path) => _col.FindById(PhotoPath.Key(path))?.ToStored();

    /// <inheritdoc />
    public IReadOnlyList<StoredMediaDetails> Under(string folder)
    {
        // Every path inside the folder begins with the folder and a separator; a folder beside it whose name
        // merely begins the same way does not.
        var prefix = PhotoPath.Key(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _col.Find(Query.StartsWith("_id", prefix)).Select(k => k.ToStored()).ToList();
    }

    /// <inheritdoc />
    public void Save(IEnumerable<StoredMediaDetails> details) => _col.Upsert(details.Select(Kept.From));

    // One file, under the key that two spellings of its path share.
    private sealed class Kept
    {
        /// <summary>The key two spellings of the path share.</summary>
        public string Id { get; set; } = default!;
        /// <summary>The file's full path.</summary>
        public string Path { get; set; } = default!;
        /// <summary>The file's size when it was read.</summary>
        public long SizeBytes { get; set; }
        // Times are stored as ticks: LiteDB rounds DateTime to milliseconds and returns it as local time.
        /// <summary>When the file had last changed when it was read.</summary>
        public long ModifiedUtcTicks { get; set; }
        /// <summary>When it was taken, as wall-clock ticks with no zone.</summary>
        public long? TakenOnTicks { get; set; }
        /// <summary>Width in pixels as displayed.</summary>
        public int Width { get; set; }
        /// <summary>Height in pixels as displayed.</summary>
        public int Height { get; set; }
        /// <summary>Playing time, for a video.</summary>
        public double? DurationSeconds { get; set; }
        /// <summary>Where it was taken: degrees north.</summary>
        public double? Latitude { get; set; }
        /// <summary>Where it was taken: degrees east.</summary>
        public double? Longitude { get; set; }
        /// <summary>The Live Photo identifier it carries.</summary>
        public string? LivePhotoId { get; set; }

        /// <summary>The details as the database keeps them.</summary>
        public static Kept From(StoredMediaDetails s) => new()
        {
            Id = PhotoPath.Key(s.Path), Path = s.Path, SizeBytes = s.SizeBytes, ModifiedUtcTicks = s.ModifiedUtc.Ticks, TakenOnTicks = s.Details.TakenOn?.Ticks,
            Width = s.Details.Width, Height = s.Details.Height, DurationSeconds = s.Details.DurationSeconds, Latitude = s.Details.Latitude, Longitude = s.Details.Longitude, LivePhotoId = s.Details.LivePhotoId,
        };

        // Capture time is the wall-clock time where the picture was taken, so it carries no zone.
        /// <summary>The details as the rest of PhotoSense knows them.</summary>
        public StoredMediaDetails ToStored() => new(Path, SizeBytes, new DateTime(ModifiedUtcTicks, DateTimeKind.Utc),
            new MediaDetails(TakenOnTicks is { } taken ? new DateTime(taken, DateTimeKind.Unspecified) : null, Width, Height, DurationSeconds, Latitude, Longitude, LivePhotoId));
    }
}
