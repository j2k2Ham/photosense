using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbPhotoRepository : IPhotoRepository, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly bool _ownsDb;
    private readonly ILiteCollection<PhotoDocument> _col;
    private readonly object _writeLock = new();
    private long _version;

    public LiteDbPhotoRepository(string databasePath = "photosense.db") : this(new LiteDatabase(databasePath), ownsDb: true) { }

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbPhotoRepository(LiteDatabase db) : this(db, ownsDb: false) { }

    private LiteDbPhotoRepository(LiteDatabase db, bool ownsDb)
    {
        _db = db;
        _ownsDb = ownsDb;
        _col = _db.GetCollection<PhotoDocument>("photos");
        _col.EnsureIndex(x => x.ContentHash);
        _col.EnsureIndex(x => x.PathKey);
        RemoveRecordsSharingAPath();
    }

    public long Version => Interlocked.Read(ref _version);

    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default)
    {
        var doc = PhotoDocument.From(photo);
        lock (_writeLock)
        {
            // A file has one record: a different record for the same path is replaced, not joined.
            foreach (var other in _col.Find(x => x.PathKey == doc.PathKey).Where(x => x.Id != doc.Id).ToList())
                _col.Delete(other.Id);
            _col.Upsert(doc);
        }
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PhotoId id, CancellationToken ct = default)
    {
        lock (_writeLock) _col.Delete(id.Value);
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default)
    {
        var list = _col.FindAll().Select(d => d.ToEntity()).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(list);
    }

    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default)
    {
        var d = _col.FindById(id.Value);
        return Task.FromResult(d?.ToEntity());
    }

    public Task<Photo?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        var key = PhotoPath.Key(path);
        var d = _col.FindOne(x => x.PathKey == key);
        return Task.FromResult(d?.ToEntity());
    }

    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        var list = _col.Find(x => x.ContentHash == hash).Select(d => d.ToEntity()).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(list);
    }

    public void Dispose()
    {
        if (_ownsDb) _db.Dispose();
    }

    // Databases written before records were keyed by path can hold several records for one file,
    // which would make a file look like a duplicate of itself. Keep one record per path.
    private void RemoveRecordsSharingAPath()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in _col.FindAll().ToList())
        {
            var key = PhotoPath.Key(doc.SourcePath);
            if (!seen.Add(key)) { _col.Delete(doc.Id); continue; }
            if (doc.PathKey == key) continue;
            doc.PathKey = key;
            _col.Update(doc);
        }
    }

    private sealed class PhotoDocument
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
}
