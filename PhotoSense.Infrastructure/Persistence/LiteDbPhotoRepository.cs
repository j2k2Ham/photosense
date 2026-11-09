using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Scans' records of each file, kept in the database, one record per file.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbPhotoRepository : IPhotoRepository, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly bool _ownsDb;
    private readonly ILiteCollection<PhotoDocument> _col;
    private readonly object _writeLock = new();
    private long _version;

    /// <summary>Opens a database of its own, which it disposes.</summary>
    public LiteDbPhotoRepository(string databasePath) : this(new LiteDatabase(databasePath), ownsDb: true) { }

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbPhotoRepository(LiteDatabase db) : this(db, ownsDb: false) { }

    private LiteDbPhotoRepository(LiteDatabase db, bool ownsDb)
    {
        _db = db;
        _ownsDb = ownsDb;
        LiteDbMapping.Prepare<PhotoDocument>();
        _col = _db.GetCollection<PhotoDocument>("photos");
        _col.EnsureIndex(x => x.ContentHash);
        _col.EnsureIndex(x => x.PathKey);
        RemoveRecordsSharingAPath();
    }

    /// <inheritdoc />
    public long Version => Interlocked.Read(ref _version);

    /// <inheritdoc />
    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
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

    /// <inheritdoc />
    public Task DeleteAsync(PhotoId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeLock) _col.Delete(id.Value);
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> ClearAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int removed;
        lock (_writeLock) removed = _col.DeleteAll();
        Interlocked.Increment(ref _version);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var list = _col.FindAll().Select(d => d.ToEntity()).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(list);
    }

    /// <inheritdoc />
    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var d = _col.FindById(id.Value);
        return Task.FromResult(d?.ToEntity());
    }

    /// <inheritdoc />
    public Task<Photo?> GetByPathAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = PhotoPath.Key(path);
        var d = _col.FindOne(x => x.PathKey == key);
        return Task.FromResult(d?.ToEntity());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var list = _col.Find(x => x.ContentHash == hash).Select(d => d.ToEntity()).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(list);
    }

    /// <summary>Disposes the database when it opened its own.</summary>
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
}
