using Moq;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Trips: home worked out or chosen, the photos taken away from it made into trips by the rules Jamie set, and each
/// trip named and given a folder. Places here lie north of home along one line, a degree some 111 km.
/// </summary>
public class TripTests
{
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly MemoryTripStore _store = new();
    private readonly Mock<IPlaceNameResolver> _places = new();
    private int _made;

    public TripTests()
    {
        _places.Setup(p => p.Locate(It.IsAny<double>(), It.IsAny<double>())).Returns((double latitude, double _) => latitude switch
        {
            < 0.5 => new PlaceMatch(latitude < 0.2 ? "Home" : "Near", "State", "US", latitude, 0),
            < 1.005 => new PlaceMatch("Far", "State", "US", 1, 0),
            < 1.5 => new PlaceMatch("Far", "State", "US", 1, 0, "Big Park", 1.01, 0),
            < 2.5 => new PlaceMatch("Other", "", "XX", 2, 0),
            < 3.02 => new PlaceMatch("Anaconda", "Montana", "US", 3, 0),
            < 4 => new PlaceMatch("Butte", "Montana", "US", 3.05, 0),
            _ => null,
        });
    }

    private Trips Trips => new(_repo, _places.Object, _store);

    private async Task<Photo> AddAsync(double latitude, DateTime taken, string? name = null, string? livePhoto = null, double? sharpness = null)
    {
        var photo = TestPhotos.Make(name ?? $"IMG_{_made++:0000}.HEIC", taken: taken);
        photo.Sharpness = sharpness;
        photo.Latitude = latitude;
        photo.Longitude = 0;
        photo.LivePhotoId = livePhoto;
        await _repo.AddOrUpdateAsync(photo, CancellationToken.None);
        return photo;
    }

    private async Task ManyAsync(int count, double latitude, DateTime from, string extension = "HEIC")
    {
        for (var n = 0; n < count; n++) await AddAsync(latitude, from.AddMinutes(n), $"IMG_{_made++:0000}.{extension}");
    }

    private async Task HomeDaysAsync(int days)
    {
        for (var day = 1; day <= days; day++) await AddAsync(0, new DateTime(2026, 1, day, 12, 0, 0));
    }

    [Test]
    public async Task Home_Is_The_Town_Photographed_On_The_Most_Days_And_Trips_Are_The_Photos_Taken_Away_From_It(CancellationToken ct)
    {
        await HomeDaysAsync(5);
        // Nearer than 50 km is not away.
        await ManyAsync(12, 0.3, new DateTime(2026, 1, 10, 9, 0, 0));
        // Two days at Far, a video and a sharp picture among them; a Live Photo's video, a place nobody knows and a photo with no time are left out.
        await ManyAsync(5, 1, new DateTime(2026, 3, 1, 9, 0, 0));
        await ManyAsync(5, 1, new DateTime(2026, 3, 2, 9, 0, 0));
        await AddAsync(1, new DateTime(2026, 3, 2, 18, 0, 0), "CLIP.MOV");
        await AddAsync(1, new DateTime(2026, 3, 2, 19, 0, 0), "SHARP.HEIC", sharpness: 900);
        await AddAsync(1, new DateTime(2026, 3, 2, 19, 0, 0), "SHARP.MOV", livePhoto: "LIVE");
        await AddAsync(9, new DateTime(2026, 3, 2, 20, 0, 0));
        await _repo.AddOrUpdateAsync(new Photo { SourcePath = "/photos/UNDATED.HEIC", FileName = "UNDATED.HEIC", Latitude = 1, Longitude = 0 }, ct);
        // More than two days later, Far again: another trip.
        await ManyAsync(10, 1, new DateTime(2026, 3, 10, 9, 0, 0));
        // A photo at home between them ends the first: six and six are each too few.
        await ManyAsync(6, 2, new DateTime(2026, 6, 1, 8, 0, 0));
        await AddAsync(0, new DateTime(2026, 6, 1, 12, 0, 0));
        await ManyAsync(6, 2, new DateTime(2026, 6, 1, 15, 0, 0));
        // Nine is too few.
        await ManyAsync(9, 2, new DateTime(2026, 8, 1, 9, 0, 0));

        var found = await Trips.FindAsync(ct);
        await Assert.That((found.Home!.Town.Town, found.Home.Chosen)).IsEqualTo(("Home", false));
        // The latest first; two in one month to one place each take their day.
        await Assert.That(found.Found.Select(t => (t.Id, t.Name, t.Folder))).IsEquivalentTo(
            [("20260310090000", "Far, State", "2026-03-10 Far"), ("20260301090000", "Far, State", "2026-03-01 Far")], CollectionOrdering.Matching);
        var first = found.Found[1];
        await Assert.That((first.Photos.Count, first.Videos, first.Start, first.End, first.Cover.FileName)).IsEqualTo((12, 1, new DateTime(2026, 3, 1, 9, 0, 0), new DateTime(2026, 3, 2, 19, 0, 0), "SHARP.HEIC"));
        await Assert.That(first.Days.Select(d => (d.Day, d.Photos.Count))).IsEquivalentTo([(new DateOnly(2026, 3, 1), 5), (new DateOnly(2026, 3, 2), 7)], CollectionOrdering.Matching);
        await Assert.That((first.Latitude, first.Longitude)).IsEqualTo((1d, 0d));
    }

    [Test]
    public async Task A_Trip_Is_Named_By_Its_Landmark_Two_Towns_Or_Its_Town_With_Its_Region(CancellationToken ct)
    {
        await HomeDaysAsync(9);
        await ManyAsync(10, 1.01, new DateTime(2026, 2, 1, 9, 0, 0));
        // Anaconda most, Butte with more than a quarter.
        await ManyAsync(7, 3, new DateTime(2026, 3, 1, 9, 0, 0));
        await ManyAsync(4, 3.05, new DateTime(2026, 3, 1, 12, 0, 0));
        // A town with no region, and only videos: the first is its cover.
        await ManyAsync(10, 2, new DateTime(2026, 4, 1, 9, 0, 0), "MOV");
        // A second town with less than a quarter does not name it.
        await ManyAsync(12, 1, new DateTime(2026, 5, 1, 9, 0, 0));
        await ManyAsync(2, 3, new DateTime(2026, 5, 1, 12, 0, 0));
        // Nor does a landmark that is not the most taken at.
        await ManyAsync(12, 1, new DateTime(2026, 6, 1, 9, 0, 0));
        await ManyAsync(5, 1.01, new DateTime(2026, 6, 1, 12, 0, 0));

        var found = (await Trips.FindAsync(ct)).Found;
        await Assert.That(found.Select(t => (t.Name, t.Folder))).IsEquivalentTo([
            ("Far, State", "2026-06 Far"), ("Far, State", "2026-05 Far"), ("Other", "2026-04 Other"), ("Anaconda and Butte", "2026-03 Anaconda and Butte"), ("Big Park", "2026-02 Big Park"),
        ], CollectionOrdering.Matching);
        await Assert.That((found[2].Videos, found[2].Cover.FileName)).IsEqualTo((10, found[2].Photos[0].FileName));
    }

    [Test]
    public async Task The_Person_Can_Choose_Home_Among_The_Towns_And_Go_Back_To_The_One_Worked_Out(CancellationToken ct)
    {
        await HomeDaysAsync(3);
        await AddAsync(1, new DateTime(2026, 2, 1, 9, 0, 0));
        await AddAsync(1, new DateTime(2026, 2, 2, 9, 0, 0));
        await ManyAsync(10, 0, new DateTime(2026, 3, 1, 9, 0, 0));
        var towns = await Trips.TownsAsync(ct);
        await Assert.That(towns.Select(t => (t.Town.Town, t.Days))).IsEquivalentTo([("Home", 4), ("Far", 2)], CollectionOrdering.Matching);

        // Far is home now: what was home is away, and makes a trip.
        await Trips.ChooseHomeAsync(towns[1].Town, ct);
        var chosen = await Trips.FindAsync(ct);
        await Assert.That((chosen.Home!.Town.Town, chosen.Home.Chosen, chosen.Found.Count)).IsEqualTo(("Far", true, 1));
        await Assert.That(chosen.Found[0].Name).IsEqualTo("Home, State");

        await Trips.ChooseHomeAsync(null, ct);
        var worked = await Trips.FindAsync(ct);
        await Assert.That((worked.Home!.Town.Town, worked.Home.Chosen, worked.Found.Count)).IsEqualTo(("Home", false, 0));
    }

    [Test]
    public async Task With_No_Photo_That_Says_Where_It_Was_Taken_There_Is_No_Home_And_No_Trip(CancellationToken ct)
    {
        await _repo.AddOrUpdateAsync(TestPhotos.Make("IMG_1.HEIC", taken: new DateTime(2026, 1, 1)), ct);
        var found = await Trips.FindAsync(ct);
        await Assert.That((found.Home, found.Found.Count)).IsEqualTo(((TripHome?)null, 0));
        await Assert.That((await Trips.TownsAsync(ct)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Distances_Are_Along_The_Earth(CancellationToken ct)
    {
        await Assert.That(Math.Round(TripRules.Km(0, 0, 1, 0), 1)).IsEqualTo(111.2);
        await Assert.That(Math.Round(TripRules.Km(45.6797, -111.0386, 46.0038, -112.5348))).IsEqualTo(121);
        await Assert.That(TripRules.Km(10, 20, 10, 20)).IsEqualTo(0);
    }
}
