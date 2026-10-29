using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Metadata;
using Directory = MetadataExtractor.Directory;

namespace PhotoSense.Tests.Infrastructure.Metadata;

/// <summary>
/// What is recorded from each shape of metadata a file can carry. The directories are built by hand, so
/// shapes that no sample file here has (a track with no size, a date that is no date) are covered too.
/// </summary>
public class MetadataReadingTests
{
    private const int CreationDate = 0x7, GpsLocation = 0xE, Model = 0x16;
    private static readonly DateTime Shot = new(2024, 4, 7, 17, 35, 59, DateTimeKind.Unspecified);

    private static Photo Read(string name, params Directory[] directories)
    {
        var photo = new Photo { SourcePath = name, FileName = name };
        BasicExifMetadataExtractor.Read(photo, directories);
        return photo;
    }

    private static Photo Picture(params Directory[] directories) => Read("IMG_1.JPG", directories);
    private static Photo Video(params Directory[] directories) => Read("IMG_1.MOV", directories);

    private static async Task AssertBare(Photo photo)
    {
        await Assert.That(photo.TakenOn).IsNull();
        await Assert.That(photo.CameraModel).IsNull();
        await Assert.That(photo.Latitude).IsNull();
        await Assert.That(photo.Longitude).IsNull();
        await Assert.That(photo.DurationSeconds).IsNull();
        await Assert.That(photo.LivePhotoId).IsNull();
        await Assert.That((photo.Width, photo.Height)).IsEqualTo((0, 0));
    }

    private static T With<T>(params (int Tag, object Value)[] tags) where T : Directory, new()
    {
        var directory = new T();
        foreach (var (tag, value) in tags) directory.Set(tag, value);
        return directory;
    }

    // ---- pictures

    [Test]
    public async Task A_Picture_With_No_Details_Records_None()
    {
        await AssertBare(Picture());
        // Capture settings but no capture date, and no camera named.
        await AssertBare(Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagIsoEquivalent, 100)), new ExifIfd0Directory()));

        var unreadable = Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, "last Tuesday")));
        await Assert.That(unreadable.TakenOn).IsNull();
    }

    [Test]
    public async Task Reads_A_Pictures_Capture_Time_And_Camera()
    {
        var picture = Picture(
            With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, "2024:04:07 17:35:59")),
            With<ExifIfd0Directory>((ExifDirectoryBase.TagModel, "iPhone 12 Pro Max")));
        await Assert.That(picture.TakenOn).IsEqualTo(Shot);
        await Assert.That(picture.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That(picture.CameraModel).IsEqualTo("iPhone 12 Pro Max");
    }

    [Test]
    [Arguments("648", 6_480_000)]            // a decimal fraction of a second
    [Arguments("6", 6_000_000)]
    [Arguments("064", 640_000)]
    [Arguments(" 648 ", 6_480_000)]
    [Arguments("6481234", 6_481_234)]        // as fine as a tick
    [Arguments("64812349999", 6_481_234)]    // finer than can be kept
    [Arguments("", 0)]
    [Arguments("   ", 0)]
    [Arguments("64a", 0)]                    // not a number: ignored
    [Arguments("-64", 0)]
    public async Task Sub_Seconds_Tell_Burst_Shots_Apart(string subSeconds, long ticksIntoTheSecond)
    {
        var picture = Picture(With<ExifSubIfdDirectory>(
            (ExifDirectoryBase.TagDateTimeOriginal, "2024:04:07 17:35:59"), (ExifDirectoryBase.TagSubsecondTimeOriginal, subSeconds)));
        await Assert.That(picture.TakenOn).IsEqualTo(Shot.AddTicks(ticksIntoTheSecond));
    }

    [Test]
    public async Task A_Capture_Time_That_Already_Has_Its_Fraction_Is_Not_Given_Another()
    {
        var picture = Picture(With<ExifSubIfdDirectory>(
            (ExifDirectoryBase.TagDateTimeOriginal, Shot.AddMilliseconds(123)), (ExifDirectoryBase.TagSubsecondTimeOriginal, "648")));
        await Assert.That(picture.TakenOn).IsEqualTo(Shot.AddMilliseconds(123));

        var whole = Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, Shot)));
        await Assert.That(whole.TakenOn).IsEqualTo(Shot);
    }

    // ---- videos

    [Test]
    public async Task A_Video_With_No_Details_Records_None()
    {
        await AssertBare(Video());
        await AssertBare(Video(new QuickTimeMovieHeaderDirectory(), new QuickTimeTrackHeaderDirectory(), new QuickTimeMetadataHeaderDirectory()));
    }

    [Test]
    public async Task A_Video_Has_A_Length_Only_When_It_Is_Longer_Than_Nothing()
    {
        await Assert.That(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.FromSeconds(84)))).DurationSeconds).IsEqualTo(84);
        await Assert.That(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.Zero))).DurationSeconds).IsNull();
        // A count of time units rather than a length of time: not understood, so left out.
        await Assert.That(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, 50_400L))).DurationSeconds).IsNull();
    }

    private static QuickTimeTrackHeaderDirectory Track(int? width, int? height, double? rotation = null)
    {
        var track = new QuickTimeTrackHeaderDirectory();
        if (width is { } w) track.Set(QuickTimeTrackHeaderDirectory.TagWidth, w);
        if (height is { } h) track.Set(QuickTimeTrackHeaderDirectory.TagHeight, h);
        if (rotation is { } r) track.Set(QuickTimeTrackHeaderDirectory.TagRotation, r);
        return track;
    }

    [Test]
    public async Task A_Videos_Size_Comes_From_The_First_Track_That_Has_One()
    {
        // Sound and data tracks come in every order; only the picture track has both a width and a height.
        var video = Video(Track(null, null), Track(0, 0), Track(1920, null), Track(null, 1080), Track(1920, 0), Track(1280, 720), Track(640, 480));
        await Assert.That((video.Width, video.Height)).IsEqualTo((1280, 720));

        var soundOnly = Video(Track(0, 0));
        await Assert.That((soundOnly.Width, soundOnly.Height)).IsEqualTo((0, 0));
    }

    [Test]
    [Arguments(null, 1920, 1080)]     // no turn noted
    [Arguments(0.0, 1920, 1080)]
    [Arguments(180.0, 1920, 1080)]    // upside down is still wide
    [Arguments(90.0, 1080, 1920)]
    [Arguments(-90.0, 1080, 1920)]
    [Arguments(270.0, 1080, 1920)]
    [Arguments(89.6, 1080, 1920)]     // as near a quarter turn as the stored numbers allow
    [Arguments(45.0, 1920, 1080)]
    public async Task A_Video_Turned_A_Quarter_Is_Taller_Than_Wide(double? rotation, int width, int height)
    {
        var video = Video(Track(1920, 1080, rotation));
        await Assert.That((video.Width, video.Height)).IsEqualTo((width, height));
    }

    [Test]
    [Arguments("+35.2886-075.5161+003.214/", 35.2886, -75.5161)]
    [Arguments("-33.8568+151.2153/", -33.8568, 151.2153)]
    [Arguments("+00.0000+078.4600/", 0.0, 78.46)]     // on the equator
    [Arguments("+51.4780+000.0000/", 51.478, 0.0)]    // on the prime meridian
    [Arguments("+35-075", 35.0, -75.0)]
    public async Task Reads_Where_A_Video_Was_Shot(string position, double latitude, double longitude)
    {
        var video = Video(With<QuickTimeMetadataHeaderDirectory>((GpsLocation, position), (Model, "iPhone 12 Pro Max")));
        await Assert.That(Math.Round(video.Latitude!.Value, 6)).IsEqualTo(Math.Round(latitude, 6));
        await Assert.That(Math.Round(video.Longitude!.Value, 6)).IsEqualTo(Math.Round(longitude, 6));
        await Assert.That(video.CameraModel).IsEqualTo("iPhone 12 Pro Max");
    }

    [Test]
    [Arguments("+00.0000+000.0000/")]   // what a phone writes with no fix
    [Arguments("somewhere nice")]
    [Arguments("35.2886-075.5161")]     // no sign on the latitude
    [Arguments("+35.2886")]
    [Arguments("")]
    public async Task A_Position_That_Is_None_Or_Makes_No_Sense_Is_Left_Out(string position)
    {
        var video = Video(With<QuickTimeMetadataHeaderDirectory>((GpsLocation, position)));
        await Assert.That(video.Latitude).IsNull();
        await Assert.That(video.Longitude).IsNull();
    }

    [Test]
    public async Task A_Videos_Date_Is_The_Phones_Own_Where_It_Wrote_One()
    {
        var localNoon = new DateTime(2023, 5, 29, 12, 0, 0, DateTimeKind.Unspecified);
        var container = With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, new DateTime(2016, 10, 28, 14, 40, 24)));

        var wallClock = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, localNoon)));
        await Assert.That(wallClock.TakenOn).IsEqualTo(localNoon);
        await Assert.That(wallClock.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);

        var written = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, "2023-05-29T12:00:00")));
        await Assert.That(written.TakenOn).IsEqualTo(localNoon);

        // A universal time is shown as the time it was here.
        var utc = new DateTime(2023, 5, 29, 16, 0, 0, DateTimeKind.Utc);
        var universal = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, utc)));
        await Assert.That(universal.TakenOn).IsEqualTo(DateTime.SpecifyKind(utc.ToLocalTime(), DateTimeKind.Unspecified));
        await Assert.That(universal.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);
    }

    [Test]
    public async Task Otherwise_A_Videos_Date_Is_The_Containers_Unless_That_Was_Never_Set()
    {
        var writtenUtc = new DateTime(2016, 10, 28, 14, 40, 24);
        var expected = DateTime.SpecifyKind(DateTime.SpecifyKind(writtenUtc, DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);
        var container = With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, writtenUtc));

        await Assert.That(Video(container).TakenOn).IsEqualTo(expected);
        await Assert.That(Video(container, new QuickTimeMetadataHeaderDirectory()).TakenOn).IsEqualTo(expected);
        await Assert.That(Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, "some time in May"))).TakenOn).IsEqualTo(expected);

        // 1904 is where a container's clock starts.
        await Assert.That(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, new DateTime(1904, 1, 1)))).TakenOn).IsNull();
        await Assert.That(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, "no date"))).TakenOn).IsNull();
        await Assert.That(Video(new QuickTimeMetadataHeaderDirectory()).TakenOn).IsNull();
    }

    // ---- through real files

    [Test]
    public async Task Odd_Files_Are_Read_For_What_They_Have()
    {
        var extractor = new BasicExifMetadataExtractor();
        async Task<Photo> ReadAsync(string name, byte[] content)
        {
            var photo = new Photo { SourcePath = name, FileName = name };
            await extractor.ExtractAsync(photo, new MemoryStream(content));
            return photo;
        }

        var still = await ReadAsync("still.MOV", MediaSamples.Movie(seconds: 0, position: "nowhere"));
        await Assert.That(still.DurationSeconds).IsNull();
        await Assert.That(still.Latitude).IsNull();
        await Assert.That(still.Width).IsEqualTo(1920);

        // A picture's details are not looked for in a video, nor a video's in a picture.
        var misnamed = await ReadAsync("IMG_1.JPG", MediaSamples.Movie());
        await AssertBare(misnamed);
        await AssertBare(await ReadAsync("IMG_1.MOV", MediaSamples.Picture()));

        var picture = await ReadAsync("IMG_1.JPG", MediaSamples.Picture());
        await Assert.That(picture.TakenOn).IsEqualTo(Shot);
    }
}
