using Trovolve.Domain.ValueObjects;

namespace Trovolve.Domain.Services;

/// <summary>A phone's own video encoder, which writes a smaller copy of a video into its library.</summary>
public interface IVideoCompressor
{
    /// <summary>
    /// Writes a copy of the video at <paramref name="path"/> made into <paramref name="target"/>, as HEVC, as a new
    /// item of the library beside it with its date and place, and answers how many bytes the copy takes. The original is
    /// left as it is.
    /// </summary>
    /// <param name="path">The video, as the phone's file system shows it.</param>
    /// <param name="target">The frame and bitrate to make it into.</param>
    /// <param name="progress">Told how far it has got, from 0 to 1.</param>
    /// <param name="ct">Stops it; nothing is kept of a copy stopped part way.</param>
    /// <exception cref="IOException">The video could not be read or written; nothing is kept.</exception>
    Task<long> CompressAsync(string path, VideoTarget target, IProgress<double> progress, CancellationToken ct);
}
