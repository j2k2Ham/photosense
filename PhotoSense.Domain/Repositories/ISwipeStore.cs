namespace PhotoSense.Domain.Repositories;

/// <summary>
/// What was decided of one picture or video in Swipe to clean. A photo is known by a key that stays as it moves: the
/// checksum of its picture where the scan worked it out, otherwise its path.
/// </summary>
/// <param name="Key">The photo.</param>
/// <param name="Delete">Marked to delete; otherwise kept.</param>
public sealed record SwipeDecision(string Key, bool Delete);

/// <summary>A day of Swipe to clean: the set drawn for it, and what was decided of it, in order.</summary>
/// <param name="Date">The day, by the phone's own calendar.</param>
/// <param name="Set">The photos drawn for the day, in the order they are shown.</param>
/// <param name="Decisions">What was decided, in the order it was; Undo takes back the last.</param>
/// <param name="Spared">Photos marked to delete that were tapped on the summary to be kept after all.</param>
/// <param name="Freed">What the last deletion of the day deleted, for its summary; null before one.</param>
public sealed record SwipeDay(DateOnly Date, IReadOnlyList<string> Set, IReadOnlyList<SwipeDecision> Decisions, IReadOnlyList<string> Spared, SwipeFreed? Freed);

/// <summary>What a deletion at the end of a session did.</summary>
/// <param name="Deleted">How many photos and videos went to the phone's Recently Deleted or bin.</param>
/// <param name="Bytes">What they took.</param>
/// <param name="Left">How many stay marked for another day.</param>
public sealed record SwipeFreed(int Deleted, long Bytes, int Left);

/// <summary>What Swipe to clean remembers on the phone: today's set, every photo ever swiped, what waits to be deleted, and the days swiped.</summary>
/// <remarks>Each method answers at once; its token is checked first.</remarks>
public interface ISwipeStore
{
    /// <summary>The day last swiped or drawn; null before the first.</summary>
    Task<SwipeDay?> DayAsync(CancellationToken ct);

    /// <summary>Keeps the day, in place of the one before.</summary>
    Task SaveDayAsync(SwipeDay day, CancellationToken ct);

    /// <summary>Every photo ever swiped, kept or marked: none of them is drawn again.</summary>
    Task<IReadOnlySet<string>> SwipedAsync(CancellationToken ct);

    /// <summary>Notes photos as swiped, or, with <paramref name="swiped"/> false, as not swiped after all.</summary>
    Task SetSwipedAsync(IEnumerable<string> keys, bool swiped, CancellationToken ct);

    /// <summary>The photos marked to delete and not deleted yet, today's and those left from days before, in the order they were marked.</summary>
    Task<IReadOnlyList<string>> MarkedAsync(CancellationToken ct);

    /// <summary>Keeps what is marked to delete, in place of what was.</summary>
    Task SaveMarkedAsync(IReadOnlyList<string> keys, CancellationToken ct);

    /// <summary>The days on which something was swiped.</summary>
    Task<IReadOnlySet<DateOnly>> DaysAsync(CancellationToken ct);

    /// <summary>Notes a day as swiped, or, with <paramref name="swiped"/> false, as not.</summary>
    Task SetDayAsync(DateOnly day, bool swiped, CancellationToken ct);
}
