namespace PhotoSense.Infrastructure.Files;

/// <summary>Compares the bytes of two files, to tell a copy from what it was copied from.</summary>
internal static class Copies
{
    /// <summary>Whether <paramref name="whole"/> begins with everything <paramref name="part"/> holds.</summary>
    public static bool BeginsWith(Stream whole, Stream part)
    {
        if (part.Length > whole.Length) return false;
        byte[] a = new byte[81920], b = new byte[81920];
        for (int read; (read = part.Read(a)) > 0;)
        {
            whole.ReadExactly(b, 0, read);
            if (!a.AsSpan(0, read).SequenceEqual(b.AsSpan(0, read))) return false;
        }
        return true;
    }
}
