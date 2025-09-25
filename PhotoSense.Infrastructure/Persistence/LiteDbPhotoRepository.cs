using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbPhotoRepository : IPhotoRepository, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<PhotoDocument> _col;

    public LiteDbPhotoRepository(string databasePath = "photosense.db")
    {
        _db = new LiteDatabase(databasePath);
        _col = _db.GetCollection<PhotoDocument>("photos");
        _col.EnsureIndex(x => x.ContentHash);
        _col.EnsureIndex(x => x.PerceptualHash);
    }

    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default)
    {
        var doc = PhotoDocument.From(photo);
        _col.Upsert(doc);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PhotoId id, CancellationToken ct = default)
    {
        _col.Delete(id.Value);
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

    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        var list = _col.Find(x => x.ContentHash == hash).Select(d => d.ToEntity()).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(list);
    }

    public void Dispose() => _db.Dispose();

    private sealed class PhotoDocument
    {
        public Guid Id { get; set; }
        public string SourcePath { get; set; } = default!;
        public string FileName { get; set; } = default!;
        public long FileSizeBytes { get; set; }
        public string? ContentHash { get; set; }
        public string? PerceptualHash { get; set; }
        public DateTime? TakenOn { get; set; }
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
            FileName = p.FileName,
            FileSizeBytes = p.FileSizeBytes,
            ContentHash = p.ContentHash,
            PerceptualHash = p.PerceptualHash,
            TakenOn = p.TakenOn,
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
                SourcePath = SourcePath,
                FileName = FileName,
                FileSizeBytes = FileSizeBytes,
                ContentHash = ContentHash,
                PerceptualHash = PerceptualHash,
                TakenOn = TakenOn,
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
