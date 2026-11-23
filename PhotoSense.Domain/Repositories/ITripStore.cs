namespace PhotoSense.Domain.Repositories;

/// <summary>A town that can be home, for Trips: photos taken far from it are away.</summary>
/// <param name="Town">The town.</param>
/// <param name="Region">The state, province or county it is in.</param>
/// <param name="Country">The country it is in.</param>
/// <param name="Latitude">Where it is: degrees north.</param>
/// <param name="Longitude">Where it is: degrees east.</param>
public sealed record HomeTown(string Town, string Region, string Country, double Latitude, double Longitude);

/// <summary>What a phone keeps for Trips: the home the person chose, if they chose one, and the names they gave trips.</summary>
public interface ITripStore
{
    /// <summary>The home the person chose; null when PhotoSense works it out.</summary>
    Task<HomeTown?> HomeAsync(CancellationToken ct);

    /// <summary>Keeps the home the person chose, or null for PhotoSense to work it out again.</summary>
    Task SaveHomeAsync(HomeTown? home, CancellationToken ct);

    /// <summary>The names the person gave trips, by each trip's id.</summary>
    Task<IReadOnlyDictionary<string, string>> NamesAsync(CancellationToken ct);

    /// <summary>Keeps the name the person gave a trip, or with null goes back to PhotoSense's.</summary>
    Task SaveNameAsync(string tripId, string? name, CancellationToken ct);
}
