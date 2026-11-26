using Trovolve.Domain.ValueObjects;

namespace Trovolve.Desktop.Imaging;

/// <summary>
/// What Windows needs from the Microsoft Store to read a kind of picture it cannot, said as the person would do it.
/// HEIC, as iPhones save, needs two free extensions: HEIF Image Extensions, which reads the file, and HEVC Video
/// Extensions, which decodes the picture inside it. WebP and AVIF have extensions of their own.
/// </summary>
public static class WindowsExtensions
{
    /// <summary>What to install, or null when nothing missing explains why a picture could not be read.</summary>
    /// <param name="format">What the picture is.</param>
    /// <param name="readerFound">Whether Windows has a reader for that kind of file at all.</param>
    /// <param name="hevcInstalled">Whether HEVC Video Extensions are installed.</param>
    public static string? Missing(PictureFormat format, bool readerFound, bool hevcInstalled) => format switch
    {
        PictureFormat.Heic when !readerFound && !hevcInstalled => "Install HEIF Image Extensions and HEVC Video Extensions from the Microsoft Store to read photos from an iPhone.",
        PictureFormat.Heic when !readerFound => "Install HEIF Image Extensions from the Microsoft Store to read photos from an iPhone.",
        PictureFormat.Heic when !hevcInstalled => "Install HEVC Video Extensions from the Microsoft Store to read photos from an iPhone.",
        PictureFormat.WebP when !readerFound => "Install Webp Image Extensions from the Microsoft Store to read WebP pictures.",
        PictureFormat.Avif when !readerFound => "Install AV1 Video Extension from the Microsoft Store to read AVIF pictures.",
        _ => null,
    };
}
