namespace Trovolve.Domain.Services;

/// <summary>A picture or video's date or place as it is to be: what is null is left as it is.</summary>
/// <param name="Path">The picture or video, as the phone's file system shows it.</param>
/// <param name="Taken">When it was taken, by the clock where it was taken; null to leave the date.</param>
/// <param name="Place">Where it was taken; null to leave the place.</param>
public sealed record DetailsChange(string Path, DateTime? Taken, PlaceChange? Place);

/// <summary>A place to give a picture or video, or none, to take its place away (as Undo does).</summary>
/// <param name="Latitude">Degrees north; null with <paramref name="Longitude"/> for no place.</param>
/// <param name="Longitude">Degrees east.</param>
public sealed record PlaceChange(double? Latitude, double? Longitude);

/// <summary>
/// The dates and places of a phone's pictures and videos, as its own photos app keeps them: on an iPhone in Photos,
/// the file left as it is; on Android in the file's own details, the picture itself untouched.
/// </summary>
public interface IPhotoDetails
{
    /// <summary>Changes them all together, after the phone asks the person once.</summary>
    /// <exception cref="IOException">One is not there, the person said no, or the phone could not change them: none changed.</exception>
    void ChangeAll(IReadOnlyList<DetailsChange> changes);

    /// <summary>The phone's own id for the picture or video at <paramref name="path"/>, which stays when it moves; null when it is not there.</summary>
    string? IdOf(string path);

    /// <summary>Whether the phone can give the picture or video at <paramref name="path"/> a place: Android writes one only into a JPEG, PNG or WebP.</summary>
    bool CanChangePlace(string path);
}
