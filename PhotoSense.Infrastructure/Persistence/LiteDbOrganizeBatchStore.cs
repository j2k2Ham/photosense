using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Every move, copy and removal, kept in the database so that it can be undone, or put back after a stop.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbOrganizeBatchStore : IOrganizeBatchStore
{
    private readonly ILiteCollection<OrganizeBatch> _col;
    private readonly ILiteCollection<KeptRecord> _records;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbOrganizeBatchStore(LiteDatabase db)
    {
        // The items are a type of their own, mapped the first time one is written: that too must not be met half-made.
        LiteDbMapping.Prepare<OrganizeBatchItem>();
        LiteDbMapping.Prepare<OrganizeBatch>();
        LiteDbMapping.Prepare<PhotoDocument>();
        LiteDbMapping.Prepare<KeptRecord>();
        _col = db.GetCollection<OrganizeBatch>("organize_batches");
        _col.EnsureIndex(x => x.UtcTicks);
        _col.EnsureIndex(x => x.State);
        // Apart from the change itself: a removal of thousands of pictures would not fit in one document with their records.
        _records = db.GetCollection<KeptRecord>("organize_batch_records");
        _records.EnsureIndex(x => x.BatchId);
    }

    /// <inheritdoc />
    public Task SaveAsync(OrganizeBatch batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _col.Upsert(batch);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<OrganizeBatch?>(_col.FindById(id));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A record written before changes had a state has none in the database; it reads as done once loaded,
    /// so the state is asked about after loading, never in the query.
    /// </remarks>
    public Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OrganizeBatch>>(_col.Query().Where(x => !x.Undone).OrderByDescending(x => x.UtcTicks).ToEnumerable()
            .Where(x => x.State == ChangeState.Done).Take(take).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrganizeBatch>> UnfinishedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OrganizeBatch>>(_col.Find(x => x.State == ChangeState.Started || x.State == ChangeState.Undoing).OrderBy(x => x.UtcTicks).ToList());
    }

    /// <inheritdoc />
    public Task MarkUndoneAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_col.FindById(id) is { } batch)
        {
            batch.Undone = true;
            _col.Update(batch);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task KeepRecordsAsync(Guid id, IReadOnlyList<Photo> records, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _records.Upsert(records.Select(r => new KeptRecord { Id = $"{id:N}/{r.Id.Value:N}", BatchId = id, Record = PhotoDocument.From(r) }));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> RecordsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Photo>>(_records.Find(x => x.BatchId == id).Select(x => x.Record.ToEntity()).ToList());
    }

    /// <summary>A scan's record of one file a removal took, kept under the removal it belongs to.</summary>
    internal sealed class KeptRecord
    {
        /// <summary>The removal's id and the record's, together.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The removal it belongs to.</summary>
        public Guid BatchId { get; set; }
        /// <summary>The record as it was before the removal.</summary>
        public PhotoDocument Record { get; set; } = default!;
    }
}
