namespace Trovolve.Domain.ValueObjects;

/// <summary>How small a compressed video is made: the qualities the person chooses among.</summary>
public enum CompressionQuality
{
    /// <summary>The video's own frame size, re-encoded as HEVC.</summary>
    High,
    /// <summary>No more than 1080 lines, as HEVC.</summary>
    Balanced,
    /// <summary>No more than 720 lines, as HEVC.</summary>
    Small,
}

/// <summary>What a video is made into: its frame size and the bitrate its picture is written at.</summary>
/// <param name="Width">The frame's width, in pixels.</param>
/// <param name="Height">The frame's height, in pixels.</param>
/// <param name="VideoBitsPerSecond">How many bits a second the picture is written with; the sound is written as it comes.</param>
public sealed record VideoTarget(int Width, int Height, long VideoBitsPerSecond);

/// <summary>
/// What each quality makes of a video, and what the copy is likely to take. The bitrate is a fixed number of bits for
/// each pixel of each frame, so that the saving said before compressing is worked out from the video itself, not from a
/// ratio: its length, its frame, and the quality chosen.
/// </summary>
public static class VideoTargets
{
    /// <summary>The bits each pixel of each frame is written with: enough for HEVC to keep a phone's video clean.</summary>
    public const double BitsPerPixel = 0.07;

    /// <summary>The frames a second the bitrate is reckoned for, as phones film.</summary>
    public const int FramesPerSecond = 30;

    /// <summary>What the sound is reckoned to take, a second.</summary>
    public const long AudioBitsPerSecond = 128_000;

    /// <summary>A copy is worth making only when it takes no more than this share of the original.</summary>
    public const double WorthIt = 0.9;

    /// <summary>The most lines a quality keeps, by the frame's shorter side; none for <see cref="CompressionQuality.High"/>.</summary>
    public static int? ShortSideOf(CompressionQuality quality) => quality switch
    {
        CompressionQuality.High => null,
        CompressionQuality.Balanced => 1080,
        _ => 720,
    };

    /// <summary>The frame and bitrate a video of <paramref name="width"/> by <paramref name="height"/> is made into at <paramref name="quality"/>.</summary>
    public static VideoTarget For(CompressionQuality quality, int width, int height)
    {
        var shortSide = Math.Min(width, height);
        var scale = ShortSideOf(quality) is { } limit && shortSide > limit ? (double)limit / shortSide : 1;
        var w = Even(width * scale);
        var h = Even(height * scale);
        return new VideoTarget(w, h, (long)Math.Round(w * (double)h * FramesPerSecond * BitsPerPixel));
    }

    /// <summary>
    /// About how many bytes the copy of a video takes at <paramref name="quality"/>; null when its frame or length is not
    /// known, or when the copy would not be worth making (more than <see cref="WorthIt"/> of <paramref name="bytes"/>).
    /// </summary>
    /// <param name="quality">The quality chosen.</param>
    /// <param name="width">The video's width, in pixels.</param>
    /// <param name="height">The video's height, in pixels.</param>
    /// <param name="seconds">How long it plays.</param>
    /// <param name="bytes">What the original takes.</param>
    public static long? Estimate(CompressionQuality quality, int width, int height, double? seconds, long bytes)
    {
        if (width <= 0 || height <= 0 || seconds is not > 0) return null;
        var estimate = (long)Math.Round((For(quality, width, height).VideoBitsPerSecond + AudioBitsPerSecond) * seconds.Value / 8);
        return estimate <= bytes * WorthIt ? estimate : null;
    }

    // Encoders take frames of an even size.
    private static int Even(double pixels) => Math.Max(2, (int)Math.Round(pixels / 2) * 2);
}
