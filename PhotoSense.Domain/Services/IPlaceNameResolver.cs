namespace PhotoSense.Domain.Services;

public interface IPlaceNameResolver
{
    /// <summary>The nearest named place to a position, in words, or null when nothing is near.</summary>
    string? Describe(double latitude, double longitude);
}
