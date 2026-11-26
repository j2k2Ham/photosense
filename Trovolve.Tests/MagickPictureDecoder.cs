using ImageMagick;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;
using Trovolve.Infrastructure.Imaging;

namespace Trovolve.Tests;

/// <summary>
/// Decodes pictures with Magick.NET, for the tests: it stands in for the system's decoder where there is none, as on
/// Linux, where the checks measure coverage, and is the yardstick the system's decoders are held to. No program ships it.
/// </summary>
internal sealed class MagickPictureDecoder : IPictureDecoder
{
    static MagickPictureDecoder()
    {
        // Files are decoded in parallel, one picture per thread; a crafted file claiming to be enormous or to hold endless
        // frames can only be unreadable.
        ResourceLimits.Thread = 1;
        ResourceLimits.Width = DecodedPicture.MaxEdge;
        ResourceLimits.Height = DecodedPicture.MaxEdge;
        ResourceLimits.ListLength = 16;
    }

    /// <summary>The analyzer the program uses, with Magick decoding: what the tests measure pictures with.</summary>
    public static PictureAnalyzer Analyzer { get; } = new(new MagickPictureDecoder());

    /// <inheritdoc />
    public Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1, Format = Enum.Parse<MagickFormat>(format.ToString()) };
            using var image = new MagickImage(picture, settings);
            image.AutoOrient();
            _ = TryStandardize(image);
            return new DecodedPicture((int)image.Width, (int)image.Height, image.GetPixels().ToByteArray(PixelMapping.RGB)!);
        }
        catch (MagickException ex) { throw new UnreadableImageException(ex.Message, ex); }
    }, ct);

    // Standard colours, from the profile a picture carries: whether they were converted. One that cannot be used leaves
    // the picture as it is.
    private static bool TryStandardize(MagickImage image)
    {
        if (image.GetColorProfile() is null) return false;
        try
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
            return true;
        }
        catch (MagickException)
        {
            return false;
        }
    }
}
