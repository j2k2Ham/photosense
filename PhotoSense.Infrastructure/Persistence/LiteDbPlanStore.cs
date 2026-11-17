using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Each day's count of removals, and the plan a paired phone shared, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbPlanStore : IPlanStore
{
    private readonly ILiteCollection<RemovedDocument> _removed;
    private readonly ILiteCollection<SharedDocument> _shared;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbPlanStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<RemovedDocument>();
        LiteDbMapping.Prepare<SharedDocument>();
        _removed = db.GetCollection<RemovedDocument>("removals_by_day");
        _shared = db.GetCollection<SharedDocument>("shared_plan");
    }

    /// <inheritdoc />
    public Task<int> RemovedOnAsync(DateOnly day, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_removed.FindById(Key(day))?.Count ?? 0);
    }

    /// <inheritdoc />
    public Task AddRemovedAsync(DateOnly day, int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // One count at a time: two removals ending together would each read the same count.
        lock (_removed)
        {
            var key = Key(day);
            _removed.Upsert(new RemovedDocument { Id = key, Count = Math.Max(0, (_removed.FindById(key)?.Count ?? 0) + count) });
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Entitlement?> SharedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_shared.FindById("shared") is { } d
            ? new Entitlement(d.Tier, d.Kind, Time(d.StartedUtcTicks), Time(d.UntilUtcTicks), d.SharedBy)
            : null);
    }

    /// <inheritdoc />
    public Task KeepSharedAsync(Entitlement? plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (plan is null) _shared.Delete("shared");
        else _shared.Upsert(new SharedDocument
        {
            Id = "shared", Tier = plan.Tier, Kind = plan.Kind, SharedBy = plan.SharedBy,
            StartedUtcTicks = plan.Started?.UtcTicks, UntilUtcTicks = plan.Until?.UtcTicks,
        });
        return Task.CompletedTask;
    }

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? Time(long? utcTicks) => utcTicks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    private sealed class RemovedDocument
    {
        /// <summary>The day, by this device's own calendar: "2026-11-13".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>How many photos and videos were removed that day.</summary>
        public int Count { get; set; }
    }

    private sealed class SharedDocument
    {
        /// <summary>The one record of a shared plan: "shared".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>Which plan.</summary>
        public PlanTier Tier { get; set; }
        /// <summary>How it is paid for.</summary>
        public PlanKind Kind { get; set; }
        /// <summary>The phone that shared it.</summary>
        public string? SharedBy { get; set; }
        /// <summary>When it began, in UTC ticks.</summary>
        public long? StartedUtcTicks { get; set; }
        /// <summary>When it renews or ends, in UTC ticks; null when it never does.</summary>
        public long? UntilUtcTicks { get; set; }
    }
}
