using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbOrganizeBatchStore : IOrganizeBatchStore
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

    public Task SaveAsync(OrganizeBatch batch, CancellationToken ct = default)
    {
        _col.Upsert(batch);
        return Task.CompletedTask;
    }

    public Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<OrganizeBatch?>(_col.FindById(id));

    // A record written before changes had a state has none in the database; it reads as done once loaded,
    // so the state is asked about after loading, never in the query.
    public Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take = 20, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrganizeBatch>>(_col.Query().Where(x => !x.Undone).OrderByDescending(x => x.UtcTicks).ToEnumerable()
            .Where(x => x.State == ChangeState.Done).Take(take).ToList());

    public Task<IReadOnlyList<OrganizeBatch>> UnfinishedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrganizeBatch>>(_col.Find(x => x.State == ChangeState.Started || x.State == ChangeState.Undoing).OrderBy(x => x.UtcTicks).ToList());

    public Task MarkUndoneAsync(Guid id, CancellationToken ct = default)
    {
        if (_col.FindById(id) is { } batch)
        {
            batch.Undone = true;
            _col.Update(batch);
        }
        return Task.CompletedTask;
    }

    public Task KeepRecordsAsync(Guid id, IReadOnlyList<Photo> records, CancellationToken ct = default)
    {
        _records.Upsert(records.Select(r => new KeptRecord { Id = $"{id:N}/{r.Id.Value:N}", BatchId = id, Record = PhotoDocument.From(r) }));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Photo>> RecordsAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Photo>>(_records.Find(x => x.BatchId == id).Select(x => x.Record.ToEntity()).ToList());

    internal sealed class KeptRecord
    {
        public string Id { get; set; } = string.Empty;
        public Guid BatchId { get; set; }
        public PhotoDocument Record { get; set; } = default!;
    }
}
