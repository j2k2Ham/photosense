using LiteDB;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;

namespace Trovolve.Infrastructure.Persistence;

/// <summary>The uses of Trovolve's features waiting to be sent, kept in the database with everything else.</summary>
/// <remarks>
/// Each use is numbered from a count kept beside the installation's id, never from the uses left, so that the numbers
/// keep rising after the uses before them are sent and forgotten. Each method answers at once; its token is checked first.
/// </remarks>
internal sealed class LiteDbUsageStore : IUsageStore
{
    private const string Key = "usage";
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<UseDocument> _uses;
    private readonly ILiteCollection<InstallDocument> _install;
    // Numbering a use and writing it are one step: two at once could take the same number.
    private readonly Lock _gate = new();

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbUsageStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<UseDocument>();
        LiteDbMapping.Prepare<InstallDocument>();
        _db = db;
        _uses = db.GetCollection<UseDocument>("usage_uses");
        _install = db.GetCollection<InstallDocument>("usage_install");
    }

    /// <inheritdoc />
    public Task<Guid> InstallAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Install().Install);
    }

    /// <inheritdoc />
    public Task<FeatureUse> AddAsync(DateTimeOffset at, string feature, int status, Guid session, int keepAtMost, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            UseDocument use = null!;
            _db.Together(() =>
            {
                var install = Install();
                install.Last++;
                _install.Upsert(install);
                use = new UseDocument { Id = install.Last, AtUtcTicks = at.UtcTicks, Feature = feature, Status = status, Session = session };
                _uses.Insert(use);
                // The numbers run on without a gap, so those past the most kept are the lowest.
                _uses.DeleteMany(u => u.Id <= install.Last - keepAtMost);
            });
            return Task.FromResult(use.ToUse());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeatureUse>> OldestAsync(int max, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<FeatureUse> oldest = [.. _uses.Query().OrderBy(u => u.Id).Limit(max).ToEnumerable().Select(u => u.ToUse())];
        return Task.FromResult(oldest);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_uses.Count());
    }

    /// <inheritdoc />
    public Task ForgetThroughAsync(long sequence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _uses.DeleteMany(u => u.Id <= sequence);
        return Task.CompletedTask;
    }

    // The installation's record, made with a new random id the first time; called under the gate.
    private InstallDocument Install()
    {
        if (_install.FindById(Key) is { } kept) return kept;
        var made = new InstallDocument { Id = Key, Install = Guid.NewGuid() };
        _install.Insert(made);
        return made;
    }

    private sealed class UseDocument
    {
        /// <summary>The use's number.</summary>
        [BsonId] public long Id { get; set; }
        /// <summary>When it was used, in UTC ticks.</summary>
        public long AtUtcTicks { get; set; }
        /// <summary>Which feature.</summary>
        public string Feature { get; set; } = string.Empty;
        /// <summary>How it ended.</summary>
        public int Status { get; set; }
        /// <summary>The run of the program it was used in.</summary>
        public Guid Session { get; set; }

        /// <summary>The use as the rest of Trovolve knows it.</summary>
        public FeatureUse ToUse() => new(Id, new DateTimeOffset(AtUtcTicks, TimeSpan.Zero), Feature, Status, Session);
    }

    private sealed class InstallDocument
    {
        /// <summary>The one record: "usage".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>This installation's id.</summary>
        public Guid Install { get; set; }
        /// <summary>The number of the last use noted.</summary>
        public long Last { get; set; }
    }
}
