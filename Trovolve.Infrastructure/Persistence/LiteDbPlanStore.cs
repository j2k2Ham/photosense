using LiteDB;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;

namespace Trovolve.Infrastructure.Persistence;

/// <summary>Each day's count of removals, the plan a paired phone shared, the day the terms of use were accepted, and a development build's plan for testing, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbPlanStore : IPlanStore
{
    private readonly ILiteCollection<RemovedDocument> _removed;
    private readonly ILiteCollection<SharedDocument> _shared;
    private readonly ILiteCollection<CleanedDocument> _cleaned;
    private readonly ILiteCollection<AcceptedDocument> _accepted;
    private readonly ILiteCollection<TestPlanDocument> _test;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbPlanStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<RemovedDocument>();
        LiteDbMapping.Prepare<SharedDocument>();
        _removed = db.GetCollection<RemovedDocument>("removals_by_day");
        _shared = db.GetCollection<SharedDocument>("shared_plan");
        LiteDbMapping.Prepare<CleanedDocument>();
        _cleaned = db.GetCollection<CleanedDocument>("cleaned_by_day");
        LiteDbMapping.Prepare<AcceptedDocument>();
        _accepted = db.GetCollection<AcceptedDocument>("terms_accepted");
        LiteDbMapping.Prepare<TestPlanDocument>();
        _test = db.GetCollection<TestPlanDocument>("test_plan");
    }

    /// <inheritdoc />
    public Task<TestPlan> TestPlanAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_test.FindById("test")?.Plan ?? TestPlan.AsBought);
    }

    /// <inheritdoc />
    public Task KeepTestPlanAsync(TestPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _test.Upsert(new TestPlanDocument { Id = "test", Plan = plan });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DateOnly?> AcceptedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_accepted.FindById("accepted") is { } d ? Day(d.Day) : (DateOnly?)null);
    }

    /// <inheritdoc />
    public Task KeepAcceptedAsync(DateOnly day, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _accepted.Upsert(new AcceptedDocument { Id = "accepted", Day = Key(day) });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<DateOnly, int>> RemovedInAsync(int year, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = Year(year);
        return Task.FromResult<IReadOnlyDictionary<DateOnly, int>>(_removed.Find(d => d.Id.StartsWith(prefix)).ToDictionary(d => Day(d.Id), d => d.Count));
    }

    /// <inheritdoc />
    public Task AddCleanedAsync(DateOnly day, int removed, long freed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // One day's record at a time: two clean-ups ending together would each read the same figures.
        lock (_cleaned)
        {
            var key = Key(day);
            var was = _cleaned.FindById(key);
            _cleaned.Upsert(new CleanedDocument { Id = key, Removed = Math.Max(0, (was?.Removed ?? 0) + removed), Freed = Math.Max(0, (was?.Freed ?? 0) + freed) });
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CleanedDay>> CleanedInAsync(int year, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = Year(year);
        return Task.FromResult<IReadOnlyList<CleanedDay>>([.. _cleaned.Find(d => d.Id.StartsWith(prefix)).Select(d => new CleanedDay(Day(d.Id), d.Removed, d.Freed)).OrderBy(d => d.Day)]);
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

    private static DateOnly Day(string key) => DateOnly.ParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Year(int year) => year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + "-";

    private static DateTimeOffset? Time(long? utcTicks) => utcTicks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    private sealed class RemovedDocument
    {
        /// <summary>The day, by this device's own calendar: "2026-11-13".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>How many photos and videos were removed that day.</summary>
        public int Count { get; set; }
    }

    private sealed class CleanedDocument
    {
        /// <summary>The day, by this device's own calendar: "2026-11-17".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>Photos and videos removed that day, on any plan.</summary>
        public int Removed { get; set; }
        /// <summary>Bytes freed from the device that day.</summary>
        public long Freed { get; set; }
    }

    private sealed class TestPlanDocument
    {
        public string Id { get; set; } = string.Empty;
        public TestPlan Plan { get; set; }
    }

    private sealed class AcceptedDocument
    {
        /// <summary>The one record of it: "accepted".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The day the terms of use were accepted: "2026-11-23".</summary>
        public string Day { get; set; } = string.Empty;
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
