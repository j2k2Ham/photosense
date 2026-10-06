using System.Globalization;

namespace PhotoSense.Functions.Api;

/// <summary>
/// The part of a file a browser asked for when playing a video ("Range: bytes=1000-"), cut down to what
/// one response may carry. A response is held in memory before it is sent, so a video is served a piece
/// at a time; the player asks for the next piece itself.
/// </summary>
public readonly record struct ByteRange(long Start, long End)
{
    /// <summary>Largest piece sent in one response.</summary>
    public const int MaxLength = 4 * 1024 * 1024;

    public long Length => End - Start + 1;

    /// <summary>
    /// Null when the request cannot be satisfied (it starts beyond the end of the file, or is not a byte
    /// range this server understands). A missing header means "from the beginning".
    /// </summary>
    public static ByteRange? Parse(string? header, long fileLength, int maxLength = MaxLength)
    {
        if (fileLength <= 0) return null;
        long start = 0, end = fileLength - 1;
        if (!string.IsNullOrWhiteSpace(header))
        {
            var value = header.Trim();
            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
            var spec = value["bytes=".Length..];
            // Several ranges at once are legal but no player sends them; they are refused rather than half-served.
            var dash = spec.IndexOf('-');
            if (dash < 0 || spec.Contains(',')) return null;
            string first = spec[..dash].Trim(), last = spec[(dash + 1)..].Trim();
            if (first.Length == 0)
            {
                // "bytes=-500": the last 500 bytes.
                if (!TryNumber(last, out var suffix) || suffix == 0) return null;
                start = Math.Max(0, fileLength - suffix);
            }
            else
            {
                if (!TryNumber(first, out start)) return null;
                if (last.Length > 0)
                {
                    if (!TryNumber(last, out var requestedEnd) || requestedEnd < start) return null;
                    end = Math.Min(requestedEnd, fileLength - 1);
                }
            }
        }
        if (start >= fileLength) return null;
        return new ByteRange(start, Math.Min(end, start + maxLength - 1));
    }

    private static bool TryNumber(string text, out long value)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public string ContentRange(long fileLength) => $"bytes {Start}-{End}/{fileLength}";
}
