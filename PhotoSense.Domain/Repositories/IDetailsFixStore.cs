namespace PhotoSense.Domain.Repositories;

/// <summary>A date or place PhotoSense gave a picture or video at the person's word, which it keeps showing whatever the file says.</summary>
/// <param name="Taken">The date given; null when only the place was.</param>
/// <param name="HasPlace">A place was given, or taken away.</param>
/// <param name="Latitude">The place given: degrees north; null for none.</param>
/// <param name="Longitude">The place given: degrees east.</param>
public sealed record FixedDetails(DateTime? Taken, bool HasPlace, double? Latitude, double? Longitude);

/// <summary>What a picture or video was before a fix and what the fix made it, for Undo.</summary>
/// <param name="Id">The phone's own id for it.</param>
/// <param name="Path">Where it was, as the phone's file system showed it.</param>
/// <param name="Before">Its date and place before.</param>
/// <param name="After">What the fix gave it.</param>
public sealed record FixedItem(string Id, string Path, FixedDetails Before, FixedDetails After);

/// <summary>The kinds of fix, each with an Undo of its own.</summary>
public enum FixKind
{
    /// <summary>Dates a camera's clock got wrong, given the date the person chose.</summary>
    Dates,
    /// <summary>Photos with no place, given the place of the one taken nearest in time.</summary>
    Places,
}

/// <summary>What a phone keeps of the fixes made to its photos' dates and places: each fixed photo by its own id, and the last fix of each kind for Undo.</summary>
public interface IDetailsFixStore
{
    /// <summary>Every fix in force, by the phone's own id for each picture or video.</summary>
    Task<IReadOnlyDictionary<string, FixedDetails>> FixedAsync(CancellationToken ct);

    /// <summary>Keeps fixes in force, in place of any for the same ids; a null takes a fix away.</summary>
    Task SaveFixedAsync(IReadOnlyDictionary<string, FixedDetails?> fixes, CancellationToken ct);

    /// <summary>The last fix of a kind, for Undo; none once undone.</summary>
    Task<IReadOnlyList<FixedItem>> LastAsync(FixKind kind, CancellationToken ct);

    /// <summary>Keeps the last fix of a kind, in place of the one before; an empty list once undone.</summary>
    Task SaveLastAsync(FixKind kind, IReadOnlyList<FixedItem> items, CancellationToken ct);
}
