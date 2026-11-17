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
}
