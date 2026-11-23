using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Swipe to clean's day, what was swiped, what waits to be deleted and the days swiped, in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbSwipeStore : ISwipeStore
{
    private const string DayId = "day";
    private const string MarkedId = "marked";
    private readonly ILiteCollection<DayDocument> _day;
    private readonly ILiteCollection<KeyDocument> _swiped;
    private readonly ILiteCollection<MarkedDocument> _marked;
    private readonly ILiteCollection<SwipedDayDocument> _days;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbSwipeStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<DayDocument>();
        LiteDbMapping.Prepare<KeyDocument>();
        LiteDbMapping.Prepare<MarkedDocument>();
        LiteDbMapping.Prepare<SwipedDayDocument>();
        _day = db.GetCollection<DayDocument>("swipe_day");
        _swiped = db.GetCollection<KeyDocument>("swipe_swiped");
        _marked = db.GetCollection<MarkedDocument>("swipe_marked");
        _days = db.GetCollection<SwipedDayDocument>("swipe_days");
    }

    /// <inheritdoc />
    public Task<SwipeDay?> DayAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_day.FindById(DayId) is { } d
            ? new SwipeDay(DateOnly.FromDayNumber(d.DayNumber), d.Set, [.. d.Decisions.Select(x => new SwipeDecision(x.Key, x.Delete))], d.Spared,
                d.FreedDeleted is { } deleted ? new SwipeFreed(deleted, d.FreedBytes, d.FreedLeft) : null)
            : null);
    }

    /// <inheritdoc />
    public Task SaveDayAsync(SwipeDay day, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _day.Upsert(new DayDocument
        {
            Id = DayId, DayNumber = day.Date.DayNumber, Set = [.. day.Set], Decisions = [.. day.Decisions.Select(d => new DecisionDocument { Key = d.Key, Delete = d.Delete })],
            Spared = [.. day.Spared], FreedDeleted = day.Freed?.Deleted, FreedBytes = day.Freed?.Bytes ?? 0, FreedLeft = day.Freed?.Left ?? 0,
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> SwipedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<string>>(_swiped.FindAll().Select(d => d.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task SetSwipedAsync(IEnumerable<string> keys, bool swiped, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var key in keys)
        {
            if (swiped) _swiped.Upsert(new KeyDocument { Id = key });
            else _swiped.Delete(key);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MarkedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(_marked.FindById(MarkedId)?.Keys ?? []);
    }

    /// <inheritdoc />
    public Task SaveMarkedAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _marked.Upsert(new MarkedDocument { Id = MarkedId, Keys = [.. keys] });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<DateOnly>> DaysAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<DateOnly>>(_days.FindAll().Select(d => DateOnly.FromDayNumber(d.Id)).ToHashSet());
    }

    /// <inheritdoc />
    public Task SetDayAsync(DateOnly day, bool swiped, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (swiped) _days.Upsert(new SwipedDayDocument { Id = day.DayNumber });
        else _days.Delete(day.DayNumber);
        return Task.CompletedTask;
    }

    /// <summary>The day last swiped or drawn: there is only ever one.</summary>
    private sealed class DayDocument
    {
        /// <summary>Always "day".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The day, as days since 1 January of year 1.</summary>
        public int DayNumber { get; set; }
        /// <summary>The photos drawn, by their keys, in order.</summary>
        public List<string> Set { get; set; } = [];
        /// <summary>What was decided, in order.</summary>
        public List<DecisionDocument> Decisions { get; set; } = [];
        /// <summary>The photos kept on the summary after being marked.</summary>
        public List<string> Spared { get; set; } = [];
        /// <summary>How many the last deletion deleted; null before one.</summary>
        public int? FreedDeleted { get; set; }
        /// <summary>What they took.</summary>
        public long FreedBytes { get; set; }
        /// <summary>How many stayed marked.</summary>
        public int FreedLeft { get; set; }
    }

    /// <summary>One decision of the day.</summary>
    private sealed class DecisionDocument
    {
        /// <summary>The photo's key.</summary>
        public string Key { get; set; } = string.Empty;
        /// <summary>Marked to delete; otherwise kept.</summary>
        public bool Delete { get; set; }
    }

    /// <summary>A photo swiped, by its key.</summary>
    private sealed class KeyDocument
    {
        /// <summary>The photo's key.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
    }

    /// <summary>What waits to be deleted: there is only ever one.</summary>
    private sealed class MarkedDocument
    {
        /// <summary>Always "marked".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The photos' keys, in the order they were marked.</summary>
        public List<string> Keys { get; set; } = [];
    }

    /// <summary>A day on which something was swiped.</summary>
    private sealed class SwipedDayDocument
    {
        /// <summary>The day, as days since 1 January of year 1.</summary>
        [BsonId] public int Id { get; set; }
    }
}
