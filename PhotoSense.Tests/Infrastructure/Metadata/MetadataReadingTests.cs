using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Metadata;
using Xunit;
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

    private static void AssertBare(Photo photo)
    {
        Assert.Null(photo.TakenOn);
        Assert.Null(photo.CameraModel);
        Assert.Null(photo.Latitude);
        Assert.Null(photo.Longitude);
        Assert.Null(photo.DurationSeconds);
        Assert.Null(photo.LivePhotoId);
        Assert.Equal((0, 0), (photo.Width, photo.Height));
    }

    private static T With<T>(params (int Tag, object Value)[] tags) where T : Directory, new()
    {
        var directory = new T();
        foreach (var (tag, value) in tags) directory.Set(tag, value);
        return directory;
    }

    // ---- pictures

    [Fact]
    public void A_Picture_With_No_Details_Records_None()
    {
        AssertBare(Picture());
        // Capture settings but no capture date, and no camera named.
        AssertBare(Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagIsoEquivalent, 100)), new ExifIfd0Directory()));

        var unreadable = Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, "last Tuesday")));
        Assert.Null(unreadable.TakenOn);
    }

    [Fact]
    public void Reads_A_Pictures_Capture_Time_And_Camera()
    {
        var picture = Picture(
            With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, "2024:04:07 17:35:59")),
            With<ExifIfd0Directory>((ExifDirectoryBase.TagModel, "iPhone 12 Pro Max")));
        Assert.Equal(Shot, picture.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, picture.TakenOn!.Value.Kind);
        Assert.Equal("iPhone 12 Pro Max", picture.CameraModel);
    }

    [Theory]
    [InlineData("648", 6_480_000)]            // a decimal fraction of a second
    [InlineData("6", 6_000_000)]
    [InlineData("064", 640_000)]
    [InlineData(" 648 ", 6_480_000)]
    [InlineData("6481234", 6_481_234)]        // as fine as a tick
    [InlineData("64812349999", 6_481_234)]    // finer than can be kept
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("64a", 0)]                    // not a number: ignored
    [InlineData("-64", 0)]
    public void Sub_Seconds_Tell_Burst_Shots_Apart(string subSeconds, long ticksIntoTheSecond)
    {
        var picture = Picture(With<ExifSubIfdDirectory>(
            (ExifDirectoryBase.TagDateTimeOriginal, "2024:04:07 17:35:59"), (ExifDirectoryBase.TagSubsecondTimeOriginal, subSeconds)));
        Assert.Equal(Shot.AddTicks(ticksIntoTheSecond), picture.TakenOn);
    }

    [Fact]
    public void A_Capture_Time_That_Already_Has_Its_Fraction_Is_Not_Given_Another()
    {
        var picture = Picture(With<ExifSubIfdDirectory>(
            (ExifDirectoryBase.TagDateTimeOriginal, Shot.AddMilliseconds(123)), (ExifDirectoryBase.TagSubsecondTimeOriginal, "648")));
        Assert.Equal(Shot.AddMilliseconds(123), picture.TakenOn);

        var whole = Picture(With<ExifSubIfdDirectory>((ExifDirectoryBase.TagDateTimeOriginal, Shot)));
        Assert.Equal(Shot, whole.TakenOn);
    }

    // ---- videos

    [Fact]
    public void A_Video_With_No_Details_Records_None()
    {
        AssertBare(Video());
        AssertBare(Video(new QuickTimeMovieHeaderDirectory(), new QuickTimeTrackHeaderDirectory(), new QuickTimeMetadataHeaderDirectory()));
    }

    [Fact]
    public void A_Video_Has_A_Length_Only_When_It_Is_Longer_Than_Nothing()
    {
        Assert.Equal(84, Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.FromSeconds(84)))).DurationSeconds);
        Assert.Null(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, TimeSpan.Zero))).DurationSeconds);
        // A count of time units rather than a length of time: not understood, so left out.
        Assert.Null(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagDuration, 50_400L))).DurationSeconds);
    }

    private static QuickTimeTrackHeaderDirectory Track(int? width, int? height, double? rotation = null)
    {
        var track = new QuickTimeTrackHeaderDirectory();
        if (width is { } w) track.Set(QuickTimeTrackHeaderDirectory.TagWidth, w);
        if (height is { } h) track.Set(QuickTimeTrackHeaderDirectory.TagHeight, h);
        if (rotation is { } r) track.Set(QuickTimeTrackHeaderDirectory.TagRotation, r);
        return track;
    }

    [Fact]
    public void A_Videos_Size_Comes_From_The_First_Track_That_Has_One()
    {
        // Sound and data tracks come in every order; only the picture track has both a width and a height.
        var video = Video(Track(null, null), Track(0, 0), Track(1920, null), Track(null, 1080), Track(1920, 0), Track(1280, 720), Track(640, 480));
        Assert.Equal((1280, 720), (video.Width, video.Height));

        var soundOnly = Video(Track(0, 0));
        Assert.Equal((0, 0), (soundOnly.Width, soundOnly.Height));
    }

    [Theory]
    [InlineData(null, 1920, 1080)]     // no turn noted
    [InlineData(0.0, 1920, 1080)]
    [InlineData(180.0, 1920, 1080)]    // upside down is still wide
    [InlineData(90.0, 1080, 1920)]
    [InlineData(-90.0, 1080, 1920)]
    [InlineData(270.0, 1080, 1920)]
    [InlineData(89.6, 1080, 1920)]     // as near a quarter turn as the stored numbers allow
    [InlineData(45.0, 1920, 1080)]
    public void A_Video_Turned_A_Quarter_Is_Taller_Than_Wide(double? rotation, int width, int height)
    {
        var video = Video(Track(1920, 1080, rotation));
        Assert.Equal((width, height), (video.Width, video.Height));
    }

    [Theory]
    [InlineData("+35.2886-075.5161+003.214/", 35.2886, -75.5161)]
    [InlineData("-33.8568+151.2153/", -33.8568, 151.2153)]
    [InlineData("+00.0000+078.4600/", 0.0, 78.46)]     // on the equator
    [InlineData("+51.4780+000.0000/", 51.478, 0.0)]    // on the prime meridian
    [InlineData("+35-075", 35.0, -75.0)]
    public void Reads_Where_A_Video_Was_Shot(string position, double latitude, double longitude)
    {
        var video = Video(With<QuickTimeMetadataHeaderDirectory>((GpsLocation, position), (Model, "iPhone 12 Pro Max")));
        Assert.Equal(latitude, video.Latitude!.Value, 6);
        Assert.Equal(longitude, video.Longitude!.Value, 6);
        Assert.Equal("iPhone 12 Pro Max", video.CameraModel);
    }

    [Theory]
    [InlineData("+00.0000+000.0000/")]   // what a phone writes with no fix
    [InlineData("somewhere nice")]
    [InlineData("35.2886-075.5161")]     // no sign on the latitude
    [InlineData("+35.2886")]
    [InlineData("")]
    public void A_Position_That_Is_None_Or_Makes_No_Sense_Is_Left_Out(string position)
    {
        var video = Video(With<QuickTimeMetadataHeaderDirectory>((GpsLocation, position)));
        Assert.Null(video.Latitude);
        Assert.Null(video.Longitude);
    }

    [Fact]
    public void A_Videos_Date_Is_The_Phones_Own_Where_It_Wrote_One()
    {
        var localNoon = new DateTime(2023, 5, 29, 12, 0, 0, DateTimeKind.Unspecified);
        var container = With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, new DateTime(2016, 10, 28, 14, 40, 24)));

        var wallClock = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, localNoon)));
        Assert.Equal(localNoon, wallClock.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, wallClock.TakenOn!.Value.Kind);

        var written = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, "2023-05-29T12:00:00")));
        Assert.Equal(localNoon, written.TakenOn);

        // A universal time is shown as the time it was here.
        var utc = new DateTime(2023, 5, 29, 16, 0, 0, DateTimeKind.Utc);
        var universal = Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, utc)));
        Assert.Equal(DateTime.SpecifyKind(utc.ToLocalTime(), DateTimeKind.Unspecified), universal.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, universal.TakenOn!.Value.Kind);
    }

    [Fact]
    public void Otherwise_A_Videos_Date_Is_The_Containers_Unless_That_Was_Never_Set()
    {
        var writtenUtc = new DateTime(2016, 10, 28, 14, 40, 24);
        var expected = DateTime.SpecifyKind(DateTime.SpecifyKind(writtenUtc, DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);
        var container = With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, writtenUtc));

        Assert.Equal(expected, Video(container).TakenOn);
        Assert.Equal(expected, Video(container, new QuickTimeMetadataHeaderDirectory()).TakenOn);
        Assert.Equal(expected, Video(container, With<QuickTimeMetadataHeaderDirectory>((CreationDate, "some time in May"))).TakenOn);

        // 1904 is where a container's clock starts.
        Assert.Null(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, new DateTime(1904, 1, 1)))).TakenOn);
        Assert.Null(Video(With<QuickTimeMovieHeaderDirectory>((QuickTimeMovieHeaderDirectory.TagCreated, "no date"))).TakenOn);
        Assert.Null(Video(new QuickTimeMetadataHeaderDirectory()).TakenOn);
    }

    // ---- through real files

    [Fact]
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
        Assert.Null(still.DurationSeconds);
        Assert.Null(still.Latitude);
        Assert.Equal(1920, still.Width);

        // A picture's details are not looked for in a video, nor a video's in a picture.
        var misnamed = await ReadAsync("IMG_1.JPG", MediaSamples.Movie());
        AssertBare(misnamed);
        AssertBare(await ReadAsync("IMG_1.MOV", MediaSamples.Picture()));

        var picture = await ReadAsync("IMG_1.JPG", MediaSamples.Picture());
        Assert.Equal(Shot, picture.TakenOn);
    }
}
