using ImageMagick;
using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Places;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>The landmark or area a picture was taken at, on top of the town it was taken in or near.</summary>
public class LandmarkTests
{
    private static readonly GeoNamesPlaceResolver Bundled = new();

    private const string TownList = "Westside\tRegion\tAA\t10.9900\t20.9900\nDateline East\t\tBB\t-17.0000\t179.9900\nArctic Town\t\tAA\t80.0\t10.0";
    private static readonly string LandmarkList = string.Join('\n',
        "Old Mill\t10.9910\t20.9910\t300",
        "Big Park\t10.9950\t20.9900\t1000",
        "Tiny Statue\t10.9900\t20.9905\t20",
        "not a row",
        "Bad\tnorth\teast\t300",
        "No Reach\t10.9900\t20.9900\tfar",
        "Date Line Pier\t-17.0000\t-179.9990\t500",
        "Polar Hut\t80.0000\t10.0300\t1000");

    private static GeoNamesPlaceResolver Places() => new(() => new StringReader(TownList), () => new StringReader(LandmarkList));

    [Test]
    public async Task Gives_The_Town_With_Where_It_Is_And_The_Nearest_Landmark_In_Reach()
    {
        var places = Places();
        var atTown = places.Locate(10.99, 20.99)!;
        await Assert.That((atTown.Town, atTown.Region, atTown.Country, Math.Round(atTown.Latitude, 2), Math.Round(atTown.Longitude, 2))).IsEqualTo(("Westside", "Region", "AA", 10.99, 20.99));
        // The statue is nearer, but its name reaches only twenty metres.
        await Assert.That((atTown.Landmark, Math.Round(atTown.LandmarkLatitude!.Value, 3), Math.Round(atTown.LandmarkLongitude!.Value, 3))).IsEqualTo(("Old Mill", 10.991, 20.991));

        await Assert.That(places.Locate(10.9945, 20.99)!.Landmark).IsEqualTo("Big Park");
        await Assert.That(places.Locate(10.99, 20.99049)!.Landmark).IsEqualTo("Tiny Statue");
    }

    [Test]
    public async Task Names_No_Landmark_Out_Of_Reach_Of_Any_And_None_Without_A_Town()
    {
        var places = Places();
        var outside = places.Locate(11.05, 20.99)!;
        await Assert.That((outside.Town, outside.Landmark, outside.LandmarkLatitude, outside.LandmarkLongitude)).IsEqualTo(("Westside", null, null, null));
        await Assert.That(places.Locate(30.0, -40.0)).IsNull();
        await Assert.That(places.Locate(double.NaN, 0)).IsNull();
        // A resolver given no list of landmarks knows none.
        await Assert.That(new GeoNamesPlaceResolver(() => new StringReader(TownList)).Locate(10.99, 20.99)!.Landmark).IsNull();
    }

    [Test]
    public async Task Finds_Landmarks_Across_The_Date_Line_And_Where_Degrees_Of_Longitude_Are_Short()
    {
        var places = Places();
        var east = places.Locate(-17.0, 179.9995)!;
        await Assert.That((east.Town, east.Region, east.Landmark)).IsEqualTo(("Dateline East", "", "Date Line Pier"));
        await Assert.That(places.Locate(80.0, 10.0)!.Landmark).IsEqualTo("Polar Hut");
    }

    [Test]
    [Arguments(44.4605, -110.8281, "Old Faithful Geyser")]
    [Arguments(48.8584, 2.2945, "Tour Eiffel")]
    [Arguments(46.8703, -113.9956, "Caras Park")]
    [Arguments(35.2505, -75.5288, "Cape Hatteras Lighthouse")]
    [Arguments(-33.8568, 151.2153, "Sydney Opera House")]
    public async Task The_Bundled_List_Knows_Sights_And_Parks(double latitude, double longitude, string landmark)
        => await Assert.That(Bundled.Locate(latitude, longitude)!.Landmark).IsEqualTo(landmark);

    [Test]
    public async Task The_Bundled_List_Names_Only_The_Town_Where_No_Landmark_Is_At_Hand()
    {
        // The middle of Anaconda, Montana: shops and houses.
        var home = Bundled.Locate(46.1283, -112.9423)!;
        await Assert.That((home.Town, home.Region, home.Country, home.Landmark)).IsEqualTo(("Anaconda", "Montana", "US", null));
        await Assert.That(Bundled.Describe(46.1283, -112.9423)).IsEqualTo("Anaconda, Montana, US");
    }

    [Test]
    public async Task A_Build_That_Left_The_Landmark_List_Out_Says_So()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => GeoNamesPlaceResolver.OpenBundledList(typeof(LandmarkTests).Assembly, GeoNamesPlaceResolver.Landmarks));
        await Assert.That(error.Message).Contains("landmarks.tsv.gz");
        using var bundled = GeoNamesPlaceResolver.OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly, GeoNamesPlaceResolver.Landmarks);
        await Assert.That(bundled.ReadLine()!.Split('\t').Length).IsEqualTo(4);
    }
}

/// <summary>Reading when and where a file was taken, and how large its picture is, without decoding it.</summary>
public sealed class ExifMediaDetailsReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-details-");
    private readonly ExifMediaDetailsReader _reader = new();

    public void Dispose() => _root.Delete(true);

    private string Put(string name, byte[] content)
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Jpeg(uint width, uint height, ushort? orientation = null, bool dated = true)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        var exif = new ExifProfile();
        if (dated) exif.SetValue(ExifTag.DateTimeOriginal, "2023:06:09 14:03:22");
        if (orientation is { } turn) exif.SetValue(ExifTag.Orientation, turn);
        image.SetProfile(exif);
        // Set on the picture as well: written from the profile alone, the tag is put back to "as recorded".
        if (orientation is { } turned) image.Orientation = (OrientationType)turned;
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    [Test]
    public async Task Reads_A_Pictures_Date_And_Size()
    {
        var details = _reader.Read(Put("IMG_1.JPG", Jpeg(64, 48)));
        await Assert.That((details.TakenOn, details.Width, details.Height, details.DurationSeconds, details.Latitude, details.Longitude))
            .IsEqualTo((new DateTime(2023, 6, 9, 14, 3, 22), 64, 48, null, null, null));
    }

    [Test]
    public async Task Gives_The_Size_As_The_Picture_Is_Shown_When_The_Camera_Was_Held_Upright()
    {
        var upright = _reader.Read(Put("IMG_2.JPG", Jpeg(64, 48, orientation: 6)));
        await Assert.That((upright.Width, upright.Height)).IsEqualTo((48, 64));
        var level = _reader.Read(Put("IMG_3.JPG", Jpeg(64, 48, orientation: 1)));
        await Assert.That((level.Width, level.Height)).IsEqualTo((64, 48));
    }

    [Test]
    public async Task Reads_A_Videos_Date_Place_Size_And_Playing_Time()
    {
        var details = _reader.Read(Put("IMG_4.MOV", MediaSamples.Movie()));
        await Assert.That((details.TakenOn, details.Width, details.Height, details.DurationSeconds)).IsEqualTo((new DateTime(2023, 5, 29, 11, 4, 45), 1920, 1080, 84.0));
        await Assert.That((Math.Round(details.Latitude!.Value, 4), Math.Round(details.Longitude!.Value, 4))).IsEqualTo((35.2886, -75.5161));
        await Assert.That(details.LivePhotoId).IsNull();
        // The identifier that ties a Live Photo's two halves together is read along with the rest.
        await Assert.That(_reader.Read(Put("IMG_5.MOV", MediaSamples.Movie("LIVE-1"))).LivePhotoId).IsEqualTo("LIVE-1");
        await Assert.That(_reader.Read(Put("IMG_5.JPG", MediaSamples.Picture("LIVE-1"))).LivePhotoId).IsEqualTo("LIVE-1");
    }

    [Test]
    public async Task A_File_That_Says_Nothing_Or_Cannot_Be_Read_Has_No_Details()
    {
        var undated = _reader.Read(Put("plain.JPG", Jpeg(40, 30, dated: false)));
        await Assert.That((undated.TakenOn, undated.Width, undated.Height)).IsEqualTo((null, 40, 30));

        var nothing = new PhotoSense.Domain.Services.MediaDetails(null, 0, 0, null, null, null);
        await Assert.That(_reader.Read(Put("broken.JPG", "not a picture at all"u8.ToArray()))).IsEqualTo(nothing);
        await Assert.That(_reader.Read(Path.Combine(_root.FullName, "missing.JPG"))).IsEqualTo(nothing);
        await Assert.That(ExifMediaDetailsReader.PixelSize([])).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Passes_Over_A_Record_Of_The_Size_That_Is_Not_A_Size()
    {
        static MetadataExtractor.Formats.Exif.ExifIfd0Directory Sized(object? width, object? height)
        {
            var directory = new MetadataExtractor.Formats.Exif.ExifIfd0Directory();
            if (width is not null) directory.Set(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagImageWidth, width);
            if (height is not null) directory.Set(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagImageHeight, height);
            return directory;
        }

        // Words where a number belongs, nothing at all, a height with no width, and a width with no height: none is a size.
        var unusable = new[] { Sized("wide", "tall"), Sized(0, 0), Sized(null, 30), Sized(40, null) };
        await Assert.That(ExifMediaDetailsReader.PixelSize(unusable)).IsEqualTo((0, 0));
        await Assert.That(ExifMediaDetailsReader.PixelSize([.. unusable, Sized(40, 30)])).IsEqualTo((40, 30));
    }
}

public class LiteDbOrganizeBatchStoreTests
{
    [Test]
    public async Task Keeps_What_A_Move_Did_Until_It_Is_Undone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = new LiteDbOrganizeBatchStore(db);
        await Assert.That(await store.RecentAsync()).IsEmpty();

        var first = new OrganizeBatch { Label = "Anaconda", Files = 2, Bytes = 9, UtcTicks = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc).Ticks,
            Items = [new OrganizeBatchItem { From = "a", To = "b", SizeBytes = 4, ModifiedUtcTicks = 7, Companion = true }], CreatedFolders = ["x"] };
        var second = new OrganizeBatch { Label = "2023", Copy = true, UtcTicks = first.UtcTicks + 1 };
        var third = new OrganizeBatch { Label = "Trips", UtcTicks = first.UtcTicks + 2 };
        foreach (var batch in new[] { first, second, third }) await store.AddAsync(batch);

        await Assert.That((await store.RecentAsync()).Select(b => b.Label)).IsEquivalentTo(new[] { "Trips", "2023", "Anaconda" }, CollectionOrdering.Matching);
        await Assert.That((await store.RecentAsync(take: 1)).Select(b => b.Label)).IsEquivalentTo(new[] { "Trips" }, CollectionOrdering.Matching);

        var kept = (await store.GetAsync(first.Id))!;
        var item = kept.Items.Single();
        await Assert.That((kept.Label, kept.Copy, kept.Files, kept.Bytes, kept.UtcTicks, kept.Undone, kept.CreatedFolders.Single())).IsEqualTo(("Anaconda", false, 2, 9L, first.UtcTicks, false, "x"));
        await Assert.That((item.From, item.To, item.SizeBytes, item.ModifiedUtcTicks, item.Companion)).IsEqualTo(("a", "b", 4L, 7L, true));
        await Assert.That(await store.GetAsync(Guid.NewGuid())).IsNull();

        // Undone, it is no longer offered, though it is still on record; undoing what is not there changes nothing.
        await store.MarkUndoneAsync(second.Id);
        await store.MarkUndoneAsync(Guid.NewGuid());
        await Assert.That((await new LiteDbOrganizeBatchStore(db).RecentAsync()).Select(b => b.Label)).IsEquivalentTo(new[] { "Trips", "Anaconda" }, CollectionOrdering.Matching);
        await Assert.That((await store.GetAsync(second.Id))!.Undone).IsTrue();
    }
}
