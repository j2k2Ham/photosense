using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Metadata;

namespace PhotoSense.Tests.Infrastructure.Metadata;

public sealed class LivePhotoAndVideoMetadataTests : IDisposable
{
    private const string LiveId = "52E3B53C-0000-4000-8000-123456789ABC";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-live-");
    private readonly BasicExifMetadataExtractor _extractor = new();

    public void Dispose() => _root.Delete(true);

    private string Put(string name, byte[] content)
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private async Task<Photo> ReadAsync(string name, byte[] content)
    {
        var photo = new Photo { SourcePath = name, FileName = name };
        await _extractor.ExtractAsync(photo, new MemoryStream(content));
        return photo;
    }

    [Test]
    public async Task Both_Halves_Of_A_Live_Photo_Carry_The_Same_Identifier()
    {
        await Assert.That(LivePhotoLink.ReadId(Put("IMG_1.JPG", MediaSamples.Picture(LiveId)))).IsEqualTo(LiveId);
        await Assert.That(LivePhotoLink.ReadId(Put("IMG_1.MOV", MediaSamples.Movie(LiveId)))).IsEqualTo(LiveId);
        await Assert.That((await ReadAsync("IMG_1.JPG", MediaSamples.Picture(LiveId))).LivePhotoId).IsEqualTo(LiveId);
        await Assert.That((await ReadAsync("IMG_1.MOV", MediaSamples.Movie(LiveId))).LivePhotoId).IsEqualTo(LiveId);
    }

    [Test]
    public async Task Ordinary_Pictures_And_Videos_Carry_None()
    {
        await Assert.That(LivePhotoLink.ReadId(Put("plain.JPG", MediaSamples.Picture()))).IsNull();
        await Assert.That(LivePhotoLink.ReadId(Put("plain.MOV", MediaSamples.Movie()))).IsNull();
        await Assert.That(LivePhotoLink.ReadId(Put("garbage.MOV", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]))).IsNull();
        await Assert.That(LivePhotoLink.ReadId(Path.Combine(_root.FullName, "not-there.JPG"))).IsNull();
        await Assert.That((await ReadAsync("plain.MOV", MediaSamples.Movie())).LivePhotoId).IsNull();
    }

    [Test]
    public async Task Reads_What_The_Phone_Recorded_About_A_Video()
    {
        var video = await ReadAsync("IMG_2.MOV", MediaSamples.Movie());
        await Assert.That(video.DurationSeconds).IsEqualTo(84);
        await Assert.That((video.Width, video.Height)).IsEqualTo((1920, 1080));
        await Assert.That(video.CameraModel).IsEqualTo("iPhone 12 Pro Max");
        await Assert.That(Math.Round(video.Latitude!.Value, 4)).IsEqualTo(35.2886);
        await Assert.That(Math.Round(video.Longitude!.Value, 4)).IsEqualTo(-75.5161);
        await Assert.That(video.TakenOn).IsNotNull();
        await Assert.That(video.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That((video.TakenOn.Value.Year, video.TakenOn.Value.Month, video.TakenOn.Value.Day)).IsEqualTo((2023, 5, 29));
    }

    [Test]
    public async Task A_Video_Shot_With_The_Phone_Upright_Is_Taller_Than_Wide()
    {
        var video = await ReadAsync("IMG_3.MOV", MediaSamples.Movie(sideways: true));
        await Assert.That((video.Width, video.Height)).IsEqualTo((1080, 1920));
    }

    [Test]
    public async Task Falls_Back_To_The_Containers_Own_Date_And_Leaves_Out_What_Is_Missing()
    {
        var written = new DateTime(2016, 10, 28, 14, 40, 24, DateTimeKind.Utc);
        var video = await ReadAsync("clip.MP4", MediaSamples.Movie(creationDate: null, position: null, model: null, writtenUtc: written));
        await Assert.That(DateTime.SpecifyKind(video.TakenOn!.Value, DateTimeKind.Local)).IsEqualTo(written.ToLocalTime());
        await Assert.That(video.Latitude).IsNull();
        await Assert.That(video.CameraModel).IsNull();

        // 1904 is what a container holds when its date was never set.
        var undated = await ReadAsync("clip.MP4", MediaSamples.Movie(creationDate: null, position: "+00.0000+000.0000/"));
        await Assert.That(undated.TakenOn).IsNull();
        await Assert.That(undated.Latitude).IsNull();

        var notAVideo = await ReadAsync("broken.MOV", [1, 2, 3, 4]);
        await Assert.That(notAVideo.DurationSeconds).IsNull();
    }

    [Test]
    public async Task A_Picture_Takes_Only_The_Video_That_Carries_Its_Identifier()
    {
        // Real files and the real reader this time: the rules themselves are covered in CompanionFileFinderTests.
        var finder = new CompanionFileFinder();
        var picture = Put("IMG_1234.JPG", MediaSamples.Picture(LiveId));
        var video = Put("IMG_1234.MOV", MediaSamples.Movie(LiveId));
        var sidecar = Put("IMG_1234.AAE", [1]);
        await Assert.That(finder.FindFor(picture).OrderBy(p => p)).IsEquivalentTo(new[] { sidecar, video }.OrderBy(p => p), CollectionOrdering.Matching);

        var other = Put("IMG_5678.JPG", MediaSamples.Picture(LiveId));
        Put("IMG_5678.MOV", MediaSamples.Movie("ANOTHER-LIVE-PHOTO"));
        await Assert.That(finder.FindFor(other)).IsEmpty();

        var plain = Put("IMG_9000.JPG", MediaSamples.Picture());
        Put("IMG_9000.MOV", MediaSamples.Movie());
        await Assert.That(finder.FindFor(plain)).IsEmpty();
    }
}
