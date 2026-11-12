namespace PhotoSense.Infrastructure.Files;

/// <summary>Compares the bytes of two files, to tell a copy from what it was copied from.</summary>
internal static class Copies
{
    /// <summary>Whether <paramref name="whole"/> begins with everything <paramref name="part"/> holds.</summary>
    public static async Task<bool> BeginsWithAsync(Stream whole, Stream part, CancellationToken ct)
    {
        if (part.Length > whole.Length) return false;
        byte[] a = new byte[81920], b = new byte[81920];
        for (int read; (read = await part.ReadAsync(a, ct)) > 0;)
        {
            await whole.ReadExactlyAsync(b.AsMemory(0, read), ct);
            if (!a.AsSpan(0, read).SequenceEqual(b.AsSpan(0, read))) return false;
        }
        return true;
    }
}
