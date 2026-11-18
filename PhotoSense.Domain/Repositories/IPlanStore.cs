using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>What this device keeps of the plan: how many photos it has removed each day, and a plan a paired phone shared with it.</summary>
public interface IPlanStore
{
    /// <summary>How many photos and videos were removed on <paramref name="day"/>, by this device's own calendar.</summary>
    Task<int> RemovedOnAsync(DateOnly day, CancellationToken ct);

    /// <summary>Adds <paramref name="count"/> to those removed on <paramref name="day"/>; a negative count takes away, down to none.</summary>
    Task AddRemovedAsync(DateOnly day, int count, CancellationToken ct);

    /// <summary>The plan a paired phone last shared with this device, or null when none has.</summary>
    Task<Entitlement?> SharedAsync(CancellationToken ct);

    /// <summary>Keeps the plan a paired phone shared, in place of any before it; null forgets it.</summary>
    Task KeepSharedAsync(Entitlement? plan, CancellationToken ct);

    /// <summary>The free plan's count of removals on each day of <paramref name="year"/> that has one.</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> RemovedInAsync(int year, CancellationToken ct);

    /// <summary>
    /// Adds to what was cleaned up on <paramref name="day"/>: photos and videos removed, Pro's too, and bytes freed from
    /// the device. A negative count takes away, down to none. Kept since growth step 7, for Year in review.
    /// </summary>
    Task AddCleanedAsync(DateOnly day, int removed, long freed, CancellationToken ct);

    /// <summary>What was cleaned up on each day of <paramref name="year"/> that has a record, first to last.</summary>
    Task<IReadOnlyList<CleanedDay>> CleanedInAsync(int year, CancellationToken ct);
}

/// <summary>What was cleaned up on a day.</summary>
/// <param name="Day">The day, by the device's own calendar.</param>
/// <param name="Removed">Photos and videos removed, on any plan.</param>
/// <param name="Freed">Bytes freed from the device.</param>
public sealed record CleanedDay(DateOnly Day, int Removed, long Freed);
