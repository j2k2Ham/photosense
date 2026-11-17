using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Application.Plans;

/// <summary>How many removals today's plan allows.</summary>
/// <param name="PerDay">How many the free plan allows each day.</param>
/// <param name="Used">How many were made today.</param>
/// <param name="Unlimited">The plan is Pro, which has no limit.</param>
public sealed record RemovalAllowance(int PerDay, int Used, bool Unlimited)
{
    /// <summary>How many more can be made today; null when there is no limit.</summary>
    public int? Left => Unlimited ? null : Math.Max(0, PerDay - Used);

    /// <summary>Whether <paramref name="count"/> more may be made today.</summary>
    public bool Allows(int count) => Unlimited || count <= Left;
}

/// <summary>
/// The free plan's limit on removals each day, the same for Clean up, Organize and Library. A removal counts once its
/// files have gone into the folder of removed files, by how many went; the count starts again at local midnight.
/// </summary>
public sealed class DailyRemovals
{
    private readonly IPlanStore _store;
    private readonly IProductSettingsSource _settings;
    private readonly Subscription _subscription;
    private readonly TimeProvider _time;

    /// <summary>Removals counted in <paramref name="store"/>, against the limit <paramref name="settings"/> give.</summary>
    /// <param name="store">Where each day's count is kept.</param>
    /// <param name="settings">The product's settings, which set the limit.</param>
    /// <param name="subscription">The plan, as Pro has no limit.</param>
    /// <param name="time">The clock whose local date a count is kept under.</param>
    public DailyRemovals(IPlanStore store, IProductSettingsSource settings, Subscription subscription, TimeProvider time)
    {
        _store = store;
        _settings = settings;
        _subscription = subscription;
        _time = time;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    // The day a moment was, by this device's own calendar.
    private DateOnly DayOf(long utcTicks) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(utcTicks, TimeSpan.Zero), _time.LocalTimeZone).DateTime);

    /// <summary>How many removals are left today.</summary>
    public async Task<RemovalAllowance> TodayAsync(CancellationToken ct)
    {
        var perDay = (await _settings.GetAsync(ct)).FreeRemovalsPerDay;
        var unlimited = (await _subscription.CurrentAsync(ct)).Pro;
        return new RemovalAllowance(perDay, await _store.RemovedOnAsync(Today, ct), unlimited);
    }

    /// <summary>Counts <paramref name="count"/> removals made just now. Pro's are not counted, so that a plan ending mid-day leaves the free ones whole.</summary>
    /// <param name="count">How many photos and videos went.</param>
    /// <param name="allowance">The allowance the removal was made under.</param>
    /// <param name="ct">Stops it before the count is kept.</param>
    public Task RecordAsync(int count, RemovalAllowance allowance, CancellationToken ct)
        => allowance.Unlimited || count <= 0 ? Task.CompletedTask : _store.AddRemovedAsync(Today, count, ct);

    /// <summary>
    /// Gives back <paramref name="count"/> removals that an undo put back (Jamie, 2026-11-13), when they were made today:
    /// a removal undone on a later day gives nothing back, as its day's count has gone.
    /// </summary>
    /// <param name="count">How many came back.</param>
    /// <param name="removedUtcTicks">When the removal was made.</param>
    /// <param name="ct">Stops it before the count changes.</param>
    public Task GiveBackAsync(int count, long removedUtcTicks, CancellationToken ct)
        => DayOf(removedUtcTicks) == Today ? _store.AddRemovedAsync(Today, -count, ct) : Task.CompletedTask;

    /// <summary>Why a removal of <paramref name="count"/> was refused, saying how many are left today.</summary>
    public static string Refusal(RemovalAllowance allowance, int count)
        => allowance.Left > 0
            ? $"You chose {count:N0} items. The free plan deletes {allowance.PerDay:N0} items a day, and you have {allowance.Left:N0} left today. Nothing was removed."
            : $"You have used today's {allowance.PerDay:N0} free removals. More are available tomorrow. Nothing was removed.";
}
