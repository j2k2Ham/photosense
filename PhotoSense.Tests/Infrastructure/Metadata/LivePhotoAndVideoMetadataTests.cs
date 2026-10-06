using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Metadata;
using Xunit;

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

    [Fact]
    public async Task Both_Halves_Of_A_Live_Photo_Carry_The_Same_Identifier()
    {
        Assert.Equal(LiveId, LivePhotoLink.ReadId(Put("IMG_1.JPG", MediaSamples.Picture(LiveId))));
        Assert.Equal(LiveId, LivePhotoLink.ReadId(Put("IMG_1.MOV", MediaSamples.Movie(LiveId))));
        Assert.Equal(LiveId, (await ReadAsync("IMG_1.JPG", MediaSamples.Picture(LiveId))).LivePhotoId);
        Assert.Equal(LiveId, (await ReadAsync("IMG_1.MOV", MediaSamples.Movie(LiveId))).LivePhotoId);
    }

    [Fact]
    public async Task Ordinary_Pictures_And_Videos_Carry_None()
    {
        Assert.Null(LivePhotoLink.ReadId(Put("plain.JPG", MediaSamples.Picture())));
        Assert.Null(LivePhotoLink.ReadId(Put("plain.MOV", MediaSamples.Movie())));
        Assert.Null(LivePhotoLink.ReadId(Put("garbage.MOV", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])));
        Assert.Null(LivePhotoLink.ReadId(Path.Combine(_root.FullName, "not-there.JPG")));
        Assert.Null((await ReadAsync("plain.MOV", MediaSamples.Movie())).LivePhotoId);
    }

    [Fact]
    public async Task Reads_What_The_Phone_Recorded_About_A_Video()
    {
        var video = await ReadAsync("IMG_2.MOV", MediaSamples.Movie());
        Assert.Equal(84, video.DurationSeconds);
        Assert.Equal((1920, 1080), (video.Width, video.Height));
        Assert.Equal("iPhone 12 Pro Max", video.CameraModel);
        Assert.Equal(35.2886, video.Latitude!.Value, 4);
        Assert.Equal(-75.5161, video.Longitude!.Value, 4);
        Assert.NotNull(video.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, video.TakenOn!.Value.Kind);
        Assert.Equal((2023, 5, 29), (video.TakenOn.Value.Year, video.TakenOn.Value.Month, video.TakenOn.Value.Day));
    }

    [Fact]
    public async Task A_Video_Shot_With_The_Phone_Upright_Is_Taller_Than_Wide()
    {
        var video = await ReadAsync("IMG_3.MOV", MediaSamples.Movie(sideways: true));
        Assert.Equal((1080, 1920), (video.Width, video.Height));
    }

    [Fact]
    public async Task Falls_Back_To_The_Containers_Own_Date_And_Leaves_Out_What_Is_Missing()
    {
        var written = new DateTime(2016, 10, 28, 14, 40, 24, DateTimeKind.Utc);
        var video = await ReadAsync("clip.MP4", MediaSamples.Movie(creationDate: null, position: null, model: null, writtenUtc: written));
        Assert.Equal(written.ToLocalTime(), DateTime.SpecifyKind(video.TakenOn!.Value, DateTimeKind.Local));
        Assert.Null(video.Latitude);
        Assert.Null(video.CameraModel);

        // 1904 is what a container holds when its date was never set.
        var undated = await ReadAsync("clip.MP4", MediaSamples.Movie(creationDate: null, position: "+00.0000+000.0000/"));
        Assert.Null(undated.TakenOn);
        Assert.Null(undated.Latitude);

        var notAVideo = await ReadAsync("broken.MOV", [1, 2, 3, 4]);
        Assert.Null(notAVideo.DurationSeconds);
    }

    [Fact]
    public void A_Picture_Takes_Only_The_Video_That_Carries_Its_Identifier()
    {
        // Real files and the real reader this time: the rules themselves are covered in CompanionFileFinderTests.
        var finder = new CompanionFileFinder();
        var picture = Put("IMG_1234.JPG", MediaSamples.Picture(LiveId));
        var video = Put("IMG_1234.MOV", MediaSamples.Movie(LiveId));
        var sidecar = Put("IMG_1234.AAE", [1]);
        Assert.Equal(new[] { sidecar, video }.OrderBy(p => p), finder.FindFor(picture).OrderBy(p => p));

        var other = Put("IMG_5678.JPG", MediaSamples.Picture(LiveId));
        Put("IMG_5678.MOV", MediaSamples.Movie("ANOTHER-LIVE-PHOTO"));
        Assert.Empty(finder.FindFor(other));

        var plain = Put("IMG_9000.JPG", MediaSamples.Picture());
        Put("IMG_9000.MOV", MediaSamples.Movie());
        Assert.Empty(finder.FindFor(plain));
    }
}
