using LiteDB;
using Trovolve.Domain.Repositories;

namespace Trovolve.Infrastructure.Persistence;

/// <summary>The fixes made to a phone's dates and places, and the last of each kind, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbDetailsFixStore : IDetailsFixStore
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<FixDocument> _fixed;
    private readonly ILiteCollection<LastDocument> _last;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbDetailsFixStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<FixDocument>();
        LiteDbMapping.Prepare<LastDocument>();
        LiteDbMapping.Prepare<ItemDocument>();
        _db = db;
        _fixed = db.GetCollection<FixDocument>("detail_fixes");
        _last = db.GetCollection<LastDocument>("detail_fix_last");
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, FixedDetails>> FixedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<string, FixedDetails>>(_fixed.FindAll().ToDictionary(d => d.Id, d => d.Details(), StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task SaveFixedAsync(IReadOnlyDictionary<string, FixedDetails?> fixes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_fixed)
            _db.Together(() =>
            {
                foreach (var (id, fix) in fixes)
                    if (fix is null) _fixed.Delete(id);
                    else _fixed.Upsert(FixDocument.Of(id, fix));
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FixedItem>> LastAsync(FixKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FixedItem>>([.. (_last.FindById(kind.ToString())?.Items ?? []).Select(i => new FixedItem(i.Id, i.Path, i.Before.Details(), i.After.Details()))]);
    }

    /// <inheritdoc />
    public Task SaveLastAsync(FixKind kind, IReadOnlyList<FixedItem> items, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _last.Upsert(new LastDocument
        {
            Id = kind.ToString(),
            Items = [.. items.Select(i => new ItemDocument { Id = i.Id, Path = i.Path, Before = FixDocument.Of(i.Id, i.Before), After = FixDocument.Of(i.Id, i.After) })],
        });
        return Task.CompletedTask;
    }

    private sealed class FixDocument
    {
        /// <summary>The phone's own id for the picture or video.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The date given, in ticks of the clock where it was taken.</summary>
        public long? TakenTicks { get; set; }
        /// <summary>A place was given or taken away.</summary>
        public bool HasPlace { get; set; }
        /// <summary>The place given: degrees north.</summary>
        public double? Latitude { get; set; }
        /// <summary>The place given: degrees east.</summary>
        public double? Longitude { get; set; }

        public static FixDocument Of(string id, FixedDetails fix)
            => new() { Id = id, TakenTicks = fix.Taken?.Ticks, HasPlace = fix.HasPlace, Latitude = fix.Latitude, Longitude = fix.Longitude };

        public FixedDetails Details() => new(TakenTicks is { } t ? new DateTime(t, DateTimeKind.Unspecified) : null, HasPlace, Latitude, Longitude);
    }

    private sealed class ItemDocument
    {
        /// <summary>The phone's own id for the picture or video.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Where it was.</summary>
        public string Path { get; set; } = string.Empty;
        /// <summary>Its date and place before.</summary>
        public FixDocument Before { get; set; } = new();
        /// <summary>What the fix gave it.</summary>
        public FixDocument After { get; set; } = new();
    }

    private sealed class LastDocument
    {
        /// <summary>The kind of fix: "Dates" or "Places".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>What it changed.</summary>
        public List<ItemDocument> Items { get; set; } = [];
    }
}
