namespace PhotoSense.Domain.Services;

/// <summary>
/// The town nearest a position, with where that town is, and the landmark or area the position is at
/// when one is known: a park, a district, a sight.
/// </summary>
public sealed record PlaceMatch(
    string Town, string Region, string Country, double Latitude, double Longitude,
    string? Landmark = null, double? LandmarkLatitude = null, double? LandmarkLongitude = null);

public interface IPlaceNameResolver
{
    /// <summary>The nearest named place to a position, in words, or null when nothing is near.</summary>
    string? Describe(double latitude, double longitude);

    /// <summary>The nearest town to a position and any landmark the position is at, or null when no town is near.</summary>
    PlaceMatch? Locate(double latitude, double longitude);
}
