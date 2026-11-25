using System.Text;

namespace Trovolve.Domain.ValueObjects;

/// <summary>The formats of picture Trovolve decodes, known by the first bytes of the file rather than its name.</summary>
public enum PictureFormat
{
    /// <summary>JPEG.</summary>
    Jpeg,
    /// <summary>PNG.</summary>
    Png,
    /// <summary>GIF.</summary>
    Gif,
    /// <summary>Windows bitmap.</summary>
    Bmp,
    /// <summary>TIFF.</summary>
    Tiff,
    /// <summary>WebP.</summary>
    WebP,
    /// <summary>HEIC or HEIF, as iPhones save.</summary>
    Heic,
    /// <summary>AVIF, the same container with another codec.</summary>
    Avif,
}

/// <summary>
/// Which picture a file really is, from its first bytes: a decoder is then told the one format to read it as, and never
/// guesses, so that a file named .jpg holding a script, a vector drawing or PostScript is never handed to their readers.
/// </summary>
public static class PictureSignature
{
    /// <summary>How many of a file's first bytes are needed to tell.</summary>
    public const int Needed = 32;

    // The brands of the ISO base media file format that are HEIF pictures, and those that are AVIF.
    private static readonly HashSet<string> HeifBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1", "heif"];
    private static readonly HashSet<string> AvifBrands = ["avif", "avis"];

    /// <summary>The format of a picture by its first bytes, or null when it is none Trovolve decodes.</summary>
    /// <param name="head">The file's first <see cref="Needed"/> bytes, or all of it when it is shorter.</param>
    public static PictureFormat? Of(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((byte[])[0xFF, 0xD8, 0xFF])) return PictureFormat.Jpeg;
        if (head.StartsWith((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A])) return PictureFormat.Png;
        if (Text(head, 0, 6) is "GIF87a" or "GIF89a") return PictureFormat.Gif;
        if (Text(head, 0, 2) == "BM") return PictureFormat.Bmp;
        if (head.StartsWith((byte[])[(byte)'I', (byte)'I', 42, 0]) || head.StartsWith((byte[])[(byte)'M', (byte)'M', 0, 42])) return PictureFormat.Tiff;
        if (Text(head, 0, 4) == "RIFF" && Text(head, 8, 4) == "WEBP") return PictureFormat.WebP;
        if (Text(head, 4, 4) == "ftyp")
        {
            // The major brand, then the compatible ones, in the file-type box at the start.
            var size = (int)Math.Min((uint)(head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3]), (uint)head.Length);
            var brands = new List<string> { Text(head, 8, 4) };
            for (var at = 16; at + 4 <= size; at += 4) brands.Add(Text(head, at, 4));
            if (brands.Exists(AvifBrands.Contains)) return PictureFormat.Avif;
            if (brands.Exists(HeifBrands.Contains)) return PictureFormat.Heic;
        }
        return null;
    }

    // Bytes as letters, or nothing when the file is too short to have them.
    private static string Text(ReadOnlySpan<byte> head, int from, int length)
        => head.Length >= from + length ? Encoding.ASCII.GetString(head.Slice(from, length)) : string.Empty;
}
