using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbOrganizeBatchStore : IOrganizeBatchStore
{
    private readonly ILiteCollection<OrganizeBatch> _col;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbOrganizeBatchStore(LiteDatabase db)
    {
        // The items are a type of their own, mapped the first time one is written: that too must not be met half-made.
        LiteDbMapping.Prepare<OrganizeBatchItem>();
        LiteDbMapping.Prepare<OrganizeBatch>();
        _col = db.GetCollection<OrganizeBatch>("organize_batches");
        _col.EnsureIndex(x => x.UtcTicks);
    }

    public Task AddAsync(OrganizeBatch batch, CancellationToken ct = default)
    {
        _col.Insert(batch);
        return Task.CompletedTask;
    }

    public Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<OrganizeBatch?>(_col.FindById(id));

    public Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take = 20, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrganizeBatch>>(_col.Query().Where(x => !x.Undone).OrderByDescending(x => x.UtcTicks).Limit(take).ToList());

    public Task MarkUndoneAsync(Guid id, CancellationToken ct = default)
    {
        if (_col.FindById(id) is { } batch)
        {
            batch.Undone = true;
            _col.Update(batch);
        }
        return Task.CompletedTask;
    }
}
