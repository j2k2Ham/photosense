using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace PhotoSense.Tests;

/// <summary>Small hand-built media files carrying the metadata an iPhone writes, for tests that read it back.</summary>
internal static class MediaSamples
{
    /// <summary>A JPEG whose Apple maker note names the Live Photo it is half of.</summary>
    public static byte[] Picture(string? livePhotoId = null)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:04:07 17:35:59");
        if (livePhotoId is not null) exif.SetValue(ExifTag.MakerNote, AppleMakerNote(livePhotoId));
        image.SetProfile(exif);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    // "Apple iOS", a version, the byte order, then one directory entry: tag 0x0011 holding the identifier as text.
    private static byte[] AppleMakerNote(string id)
    {
        var text = Encoding.ASCII.GetBytes(id + "\0");
        using var note = new MemoryStream();
        note.Write("Apple iOS\0"u8);
        note.Write([0x00, 0x01, (byte)'M', (byte)'M']);
        note.Write(U16(1));                              // one entry
        note.Write(U16(0x0011)); note.Write(U16(2));     // tag, type ASCII
        note.Write(U32((uint)text.Length));
        note.Write(U32(14 + 2 + 12 + 4));                // where the text starts, counted from the start of the note
        note.Write(U32(0));                              // no further directory
        note.Write(text);
        return note.ToArray();
    }

    /// <summary>A QuickTime movie with no picture data: header, one video track, and the phone's metadata.</summary>
    public static byte[] Movie(string? livePhotoId = null, string? creationDate = "2023-05-29T11:04:45-0400", string? position = "+35.2886-075.5161+003.214/",
        string? model = "iPhone 12 Pro Max", uint width = 1920, uint height = 1080, bool sideways = false, uint seconds = 84, DateTime? writtenUtc = null)
    {
        const uint timescale = 600;
        var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var written = writtenUtc is { } w ? (uint)(w - epoch).TotalSeconds : 0u;

        // Unrotated, or turned a quarter: [0 1 0; -1 0 0; 0 0 1] in 16.16 and 2.30 fixed point.
        byte[] matrix = sideways
            ? [.. U32(0), .. U32(0x00010000), .. U32(0), .. U32(0xFFFF0000), .. U32(0), .. U32(0), .. U32(0), .. U32(0), .. U32(0x40000000)]
            : [.. U32(0x00010000), .. U32(0), .. U32(0), .. U32(0), .. U32(0x00010000), .. U32(0), .. U32(0), .. U32(0), .. U32(0x40000000)];

        var mvhd = Atom("mvhd", [.. U32(0), .. U32(written), .. U32(written), .. U32(timescale), .. U32(seconds * timescale), .. U32(0x00010000), .. U16(0x0100),
            .. new byte[10], .. matrix, .. new byte[24], .. U32(2)]);
        var tkhd = Atom("tkhd", [.. U32(1), .. U32(written), .. U32(written), .. U32(1), .. U32(0), .. U32(seconds * timescale), .. new byte[8],
            .. U16(0), .. U16(0), .. U16(0), .. U16(0), .. matrix, .. U32(width << 16), .. U32(height << 16)]);

        var entries = new List<(string Key, string Value)>();
        if (creationDate is not null) entries.Add(("com.apple.quicktime.creationdate", creationDate));
        if (position is not null) entries.Add(("com.apple.quicktime.location.ISO6709", position));
        if (model is not null) entries.Add(("com.apple.quicktime.model", model));
        if (livePhotoId is not null) entries.Add(("com.apple.quicktime.content.identifier", livePhotoId));

        var hdlr = Atom("hdlr", [.. U32(0), .. U32(0), .. "mdta"u8, .. new byte[12], 0]);
        var keys = Atom("keys", [.. U32(0), .. U32((uint)entries.Count),
            .. entries.SelectMany(e => (byte[])[.. U32((uint)(8 + e.Key.Length)), .. "mdta"u8, .. Encoding.ASCII.GetBytes(e.Key)])]);
        var ilst = Atom("ilst", entries.SelectMany((e, i) =>
            Atom(U32((uint)(i + 1)), Atom("data", [.. U32(1), .. U32(0), .. Encoding.UTF8.GetBytes(e.Value)]))).ToArray());
        var meta = Atom("meta", [.. hdlr, .. keys, .. ilst]);

        return [.. Atom("ftyp", [.. "qt  "u8, .. U32(0), .. "qt  "u8]), .. Atom("moov", [.. mvhd, .. Atom("trak", tkhd), .. meta])];
    }

    private static byte[] Atom(string type, byte[] body) => Atom(Encoding.ASCII.GetBytes(type), body);
    private static byte[] Atom(byte[] type, byte[] body) => [.. U32((uint)(8 + body.Length)), .. type, .. body];

    private static byte[] U16(ushort value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); return b; }
    private static byte[] U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }
}
