namespace PhotoSense.Domain.Services;

/// <summary>
/// The town nearest a position, with where that town is, and the landmark or area the position is at
/// when one is known: a park, a district, a sight.
/// </summary>
/// <param name="Town">The nearest town's name.</param>
/// <param name="Region">The state, province or county it is in.</param>
/// <param name="Country">The country it is in.</param>
/// <param name="Latitude">Where the town is: degrees north.</param>
/// <param name="Longitude">Where the town is: degrees east.</param>
/// <param name="Landmark">The landmark or area the position is at, when one is known.</param>
/// <param name="LandmarkLatitude">Where the landmark is: degrees north.</param>
/// <param name="LandmarkLongitude">Where the landmark is: degrees east.</param>
public sealed record PlaceMatch(
    string Town, string Region, string Country, double Latitude, double Longitude,
    string? Landmark = null, double? LandmarkLatitude = null, double? LandmarkLongitude = null);

/// <summary>Names the place a position is at, from place names kept on this machine.</summary>
public interface IPlaceNameResolver
{
    /// <summary>The nearest named place to a position, in words, or null when nothing is near.</summary>
    string? Describe(double latitude, double longitude);

    /// <summary>The nearest town to a position and any landmark the position is at, or null when no town is near.</summary>
    PlaceMatch? Locate(double latitude, double longitude);
}
