namespace PhotoSense.Domain.Services;

/// <summary>What a picture or video says about itself, read from the file without decoding it.</summary>
/// <param name="LivePhotoId">The identifier an iPhone writes into both halves of a Live Photo; absent from anything else.</param>
public sealed record MediaDetails(DateTime? TakenOn, int Width, int Height, double? DurationSeconds, double? Latitude, double? Longitude, string? LivePhotoId = null);

public interface IMediaDetailsReader
{
    /// <summary>Reads what the file records. A file that cannot be read yields details with nothing in them.</summary>
    MediaDetails Read(string path);
}
