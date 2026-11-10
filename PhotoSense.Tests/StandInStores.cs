using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests;

/// <summary>
/// The changes kept so that they can be undone, in memory, standing in for the database. Each change is copied as it
/// is saved and as it is read, as a database would, so that what is kept is what was saved, whatever happens to it after.
/// </summary>
internal sealed class MemoryBatchStore : IOrganizeBatchStore
{
    private readonly Dictionary<Guid, OrganizeBatch> _batches = [];
    private readonly Dictionary<Guid, List<Photo>> _records = [];

    /// <summary>What is kept of a change at this moment: for a hook that runs in the middle of a move, where nothing can be awaited.</summary>
    public OrganizeBatch? Stored(Guid id)
    {
        lock (_batches) return _batches.TryGetValue(id, out var batch) ? Copy(batch) : null;
    }

    /// <inheritdoc />
    public Task SaveAsync(OrganizeBatch batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_batches) _batches[batch.Id] = Copy(batch);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Stored(id));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_batches)
            return Task.FromResult<IReadOnlyList<OrganizeBatch>>(_batches.Values.Where(b => !b.Undone && b.State == ChangeState.Done)
                .OrderByDescending(b => b.UtcTicks).Take(take).Select(Copy).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrganizeBatch>> UnfinishedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_batches)
            return Task.FromResult<IReadOnlyList<OrganizeBatch>>(_batches.Values.Where(b => b.State is ChangeState.Started or ChangeState.Undoing)
                .OrderBy(b => b.UtcTicks).Select(Copy).ToList());
    }

    /// <inheritdoc />
    public Task MarkUndoneAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_batches)
            if (_batches.TryGetValue(id, out var batch)) batch.Undone = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task KeepRecordsAsync(Guid id, IReadOnlyList<Photo> records, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_records)
        {
            var kept = _records.TryGetValue(id, out var list) ? list : _records[id] = [];
            // A record kept again under the same change replaces the one kept before, as the database's key does.
            kept.RemoveAll(r => records.Any(n => n.Id == r.Id));
            kept.AddRange(records);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> RecordsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_records) return Task.FromResult<IReadOnlyList<Photo>>(_records.TryGetValue(id, out var kept) ? [.. kept] : []);
    }

    private static OrganizeBatch Copy(OrganizeBatch b) => new()
    {
        Id = b.Id, UtcTicks = b.UtcTicks, Label = b.Label, Copy = b.Copy, Removed = b.Removed, Files = b.Files, Bytes = b.Bytes, Undone = b.Undone, State = b.State,
        Items = b.Items.Select(i => new OrganizeBatchItem { From = i.From, To = i.To, SizeBytes = i.SizeBytes, ModifiedUtcTicks = i.ModifiedUtcTicks, Companion = i.Companion, Back = i.Back }).ToList(),
        CreatedFolders = [.. b.CreatedFolders], UndoFolders = [.. b.UndoFolders],
    };
}

/// <summary>What has been read from each file for Organize, in memory, under the key two spellings of a path share.</summary>
internal sealed class MemoryMediaDetailsStore : IMediaDetailsStore
{
    private readonly Dictionary<string, StoredMediaDetails> _details = [];

    /// <inheritdoc />
    public StoredMediaDetails? At(string path)
    {
        lock (_details) return _details.GetValueOrDefault(PhotoPath.Key(path));
    }

    /// <inheritdoc />
    public IReadOnlyList<StoredMediaDetails> Under(string folder)
    {
        var prefix = PhotoPath.Key(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_details) return _details.Where(d => d.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(d => d.Value).ToList();
    }

    /// <inheritdoc />
    public void Save(IEnumerable<StoredMediaDetails> details)
    {
        lock (_details)
            foreach (var d in details) _details[PhotoPath.Key(d.Path)] = d;
    }
}

/// <summary>The record of scans run, in memory.</summary>
internal sealed class MemoryScanHistory : IScanHistory
{
    private readonly List<ScanRecord> _scans = [];

    /// <inheritdoc />
    public Task AddAsync(ScanRecord scan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_scans) _scans.Add(scan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScanRecord>> RecentAsync(int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_scans) return Task.FromResult<IReadOnlyList<ScanRecord>>(_scans.OrderByDescending(s => s.StartedUtc).Take(take).ToList());
    }
}

/// <summary>The record of what was done to photos, in memory, each entry stamped from the clock given, as the store stamps it.</summary>
internal sealed class MemoryAudit(TimeProvider time) : IAuditRepository
{
    private readonly List<AuditEntry> _entries = [];

    /// <summary>What was recorded, oldest first.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_entries) return [.. _entries]; }
    }

    /// <inheritdoc />
    public Task AddAsync(AuditEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_entries) _entries.Add(entry with { UtcTimestamp = time.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }
}

/// <summary>Every store a program keeps, replaced by a stand-in in memory, so that no test but the stores' own touches the database.</summary>
internal static class StandInStores
{
    /// <summary>Registers a stand-in for each store, in place of the database's; the program's stores are never made.</summary>
    /// <param name="services">The program's registrations, made already.</param>
    /// <param name="pairs">The pairings to keep, when the test has its own.</param>
    public static IServiceCollection AddStandInStores(this IServiceCollection services, IPairingStore? pairs = null)
        => services.AddSingleton<IPhotoRepository>(new InMemoryPhotoRepository())
            .AddSingleton<IOrganizeBatchStore>(new MemoryBatchStore())
            .AddSingleton<IMediaDetailsStore>(new MemoryMediaDetailsStore())
            .AddSingleton<IScanHistory>(new MemoryScanHistory())
            .AddSingleton<IAuditRepository>(sp => new MemoryAudit(sp.GetRequiredService<TimeProvider>()))
            .AddSingleton<IPairingStore>(pairs ?? new MemoryPairs("this-device"));
}
