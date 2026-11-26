using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Desktop.Imaging;

/// <summary>
/// The decoder of a computer whose system has none Trovolve uses: Trovolve reads pictures with Windows' own decoder or a
/// Mac's, and runs elsewhere only for its checks. Every picture is unreadable, saying so.
/// </summary>
public sealed class UnavailablePictureDecoder : IPictureDecoder
{
    /// <inheritdoc />
    public Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, int maxEdge, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new UnreadableImageException("Trovolve reads pictures on Windows and on a Mac, with the system's own decoder; this computer has none it can use.");
    }
}
