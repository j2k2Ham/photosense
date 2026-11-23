using System.Text;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests.Domain;

/// <summary>Telling a picture's format by its first bytes, whatever its name says.</summary>
public sealed class PictureSignatureTests
{
    // An ISO base media file's first box, the file type, with its major brand and compatible ones.
    private static byte[] FileType(string major, params string[] compatible)
    {
        var size = 16 + 4 * compatible.Length;
        return [0, 0, 0, (byte)size, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(major), 0, 0, 0, 0, .. compatible.SelectMany(Encoding.ASCII.GetBytes)];
    }

    [Test]
    public async Task Knows_Each_Format_Trovolve_Decodes_By_Its_First_Bytes(CancellationToken ct)
    {
        await Assert.That(PictureSignature.Of([0xFF, 0xD8, 0xFF, 0xE1, 0, 0])).IsEqualTo(PictureFormat.Jpeg);
        await Assert.That(PictureSignature.Of([0x89, .. "PNG"u8, 0x0D, 0x0A, 0x1A, 0x0A, 0])).IsEqualTo(PictureFormat.Png);
        await Assert.That((PictureSignature.Of("GIF87a.."u8), PictureSignature.Of("GIF89a.."u8))).IsEqualTo((PictureFormat.Gif, PictureFormat.Gif));
        await Assert.That(PictureSignature.Of("BM......"u8)).IsEqualTo(PictureFormat.Bmp);
        await Assert.That((PictureSignature.Of([(byte)'I', (byte)'I', 42, 0, 8]), PictureSignature.Of([(byte)'M', (byte)'M', 0, 42, 0]))).IsEqualTo((PictureFormat.Tiff, PictureFormat.Tiff));
        await Assert.That(PictureSignature.Of("RIFF\0\0\0\0WEBPVP8 "u8)).IsEqualTo(PictureFormat.WebP);
        // An iPhone's HEIC, a HEIF named by a compatible brand, and AVIF, which shares the container.
        await Assert.That(PictureSignature.Of(FileType("heic", "mif1", "heic"))).IsEqualTo(PictureFormat.Heic);
        await Assert.That(PictureSignature.Of(FileType("isom", "mif1"))).IsEqualTo(PictureFormat.Heic);
        await Assert.That(PictureSignature.Of(FileType("avif", "mif1"))).IsEqualTo(PictureFormat.Avif);
    }

    [Test]
    public async Task Knows_None_Of_What_Is_Not_A_Picture_It_Decodes_Whatever_It_Is_Called(CancellationToken ct)
    {
        // A vector drawing, PostScript, ImageMagick's own scripts, a video, plain text, a RIFF that is not WebP, and too little to tell.
        foreach (var text in new[] { "<?xml version=\"1.0\"?><svg>", "%!PS-Adobe-3.0", "push graphic-context", "<image><read filename=\"/etc/passwd\"/>", "not an image at all", "RIFF\0\0\0\0WAVEfmt ", "BMP"[..1] })
            await Assert.That(PictureSignature.Of(Encoding.ASCII.GetBytes(text))).IsNull();
        await Assert.That(PictureSignature.Of(FileType("qt  ", "qt  "))).IsNull();
        await Assert.That(PictureSignature.Of(FileType("mp42", "isom", "mp41"))).IsNull();
        await Assert.That(PictureSignature.Of([])).IsNull();
        // A file-type box saying it is longer than what was read is read as far as there is.
        var cut = FileType("isom", "mif1");
        cut[3] = 200;
        await Assert.That(PictureSignature.Of(cut)).IsEqualTo(PictureFormat.Heic);
    }
}
