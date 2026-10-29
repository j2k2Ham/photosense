using PhotoSense.Infrastructure.Metadata;

namespace PhotoSense.Tests.Infrastructure.Metadata;

public sealed class LivePhotoIdCacheTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-liveids-");
    public void Dispose() => _root.Delete(true);

    [Test]
    public async Task Reads_A_Files_Identifier_Once_For_As_Long_As_The_File_Stays_The_Same()
    {
        var path = Path.Combine(_root.FullName, "IMG_1.HEIC");
        File.WriteAllText(path, "AAAA");
        var reads = new List<string>();
        var cache = new LivePhotoIdCache(p => { reads.Add(p); return File.Exists(p) ? File.ReadAllText(p) : null; });

        await Assert.That((cache.Read(path), cache.Read(path), reads.Count)).IsEqualTo(("AAAA", "AAAA", 1));

        // Another size, or the same size written at another time: read again.
        File.WriteAllText(path, "BBBBBB");
        await Assert.That((cache.Read(path), reads.Count)).IsEqualTo(("BBBBBB", 2));
        File.WriteAllText(path, "CCCCCC");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        await Assert.That((cache.Read(path), cache.Read(path), reads.Count)).IsEqualTo(("CCCCCC", "CCCCCC", 3));

        // A file with no identifier is remembered as having none; one that is not there is left to the reader each time.
        var plain = Path.Combine(_root.FullName, "plain.JPG");
        File.WriteAllText(plain, "x");
        var none = new LivePhotoIdCache(p => { reads.Add(p); return null; });
        await Assert.That((none.Read(plain), none.Read(plain), reads.Count)).IsEqualTo((null, null, 4));
        var missing = Path.Combine(_root.FullName, "missing.JPG");
        await Assert.That((cache.Read(missing), cache.Read(missing), reads.Count)).IsEqualTo((null, null, 6));
    }

    [Test]
    public async Task Reads_Real_Files_When_Given_No_Reader_Of_Its_Own()
    {
        var path = Path.Combine(_root.FullName, "IMG_1.JPG");
        File.WriteAllBytes(path, MediaSamples.Picture("LIVE-1"));
        await Assert.That(new LivePhotoIdCache().Read(path)).IsEqualTo("LIVE-1");
    }
}
