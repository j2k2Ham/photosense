namespace PhotoSense.Domain.Services;

/// <summary>What a picture or video says about itself, read from the file without decoding it.</summary>
/// <param name="TakenOn">When it was taken, as the file records it.</param>
/// <param name="Width">Width in pixels as displayed; 0 when the file does not say.</param>
/// <param name="Height">Height in pixels as displayed; 0 when the file does not say.</param>
/// <param name="DurationSeconds">Playing time, for a video.</param>
/// <param name="Latitude">Where it was taken: degrees north, negative for south.</param>
/// <param name="Longitude">Where it was taken: degrees east, negative for west.</param>
/// <param name="LivePhotoId">The identifier an iPhone writes into both halves of a Live Photo; absent from anything else.</param>
public sealed record MediaDetails(DateTime? TakenOn, int Width, int Height, double? DurationSeconds, double? Latitude, double? Longitude, string? LivePhotoId = null);

/// <summary>Reads when and where a picture or video was taken, for Organize, without a scan.</summary>
public interface IMediaDetailsReader
{
    /// <summary>Reads what the file records. A file that cannot be read yields details with nothing in them.</summary>
    Task<MediaDetails> ReadAsync(string path, CancellationToken ct);
}
