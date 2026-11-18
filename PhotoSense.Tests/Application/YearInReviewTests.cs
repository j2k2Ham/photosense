using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Year in review: the year's photos and videos, its trips, the busiest month, and what was cleaned up,
/// offered in December and January; and the record of what each day cleaned up that it reads.
/// </summary>
public class YearInReviewTests
{
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly MemoryPlanStore _plans = new();
    private readonly MemorySwipeStore _swipes = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 12, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IPlaceNameResolver> _places = new();

    public YearInReviewTests()
    {
        // Butte is east of 0°, Glacier west of it: where a photo was taken is told by its longitude alone.
        _places.Setup(p => p.Locate(It.IsAny<double>(), It.Is<double>(x => x > 0))).Returns(new PlaceMatch("Butte", "Montana", "US", 46, -112.5));
        _places.Setup(p => p.Locate(It.IsAny<double>(), It.Is<double>(x => x < 0))).Returns(new PlaceMatch("West Glacier", "Montana", "US", 48.5, -114));
    }

    private YearInReview Review => new(_repo, _places.Object, _plans, _swipes, new Trips(_repo, _places.Object, new MemoryTripStore()), _clock);

    private async Task AddAsync(string name, DateTime taken, double? longitude = null, string? livePhoto = null)
    {
        var photo = TestPhotos.Make(name, taken: taken);
        photo.Latitude = longitude is null ? null : 46;
        photo.Longitude = longitude;
        photo.LivePhotoId = livePhoto;
        await _repo.AddOrUpdateAsync(photo, CancellationToken.None);
    }

    [Test]
    public async Task It_Is_Offered_In_December_For_The_Year_Ending_And_In_January_For_The_Year_Just_Ended(CancellationToken ct)
    {
        await Assert.That(Review.Due()).IsEqualTo(2026);
        _clock.Now = new DateTimeOffset(2027, 1, 20, 9, 0, 0, TimeSpan.Zero);
        await Assert.That(Review.Due()).IsEqualTo(2026);
        _clock.Now = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);
        await Assert.That(Review.Due()).IsNull();
    }

    [Test]
    public async Task The_Years_Photos_Videos_And_Busiest_Month_Are_Counted_A_Live_Photo_Once(CancellationToken ct)
    {
        await AddAsync("IMG_1.HEIC", new DateTime(2026, 7, 4), longitude: -1);
        await AddAsync("IMG_2.HEIC", new DateTime(2026, 7, 5), longitude: -1);
        await AddAsync("IMG_3.HEIC", new DateTime(2026, 7, 6), longitude: 1);
        await AddAsync("IMG_4.MOV", new DateTime(2026, 3, 1), longitude: 1);
        await AddAsync("IMG_5.HEIC", new DateTime(2026, 3, 2), livePhoto: "LIVE");
        await AddAsync("IMG_5.MOV", new DateTime(2026, 3, 2), livePhoto: "LIVE");
        await AddAsync("IMG_6.HEIC", new DateTime(2025, 7, 1), longitude: 1);

        var year = await Review.ReviewAsync(2026, ct);
        // Two photos away are too few for a trip.
        await Assert.That((year.Year, year.Photos, year.Videos, year.Trips, year.TopTrips.Count, year.BusiestMonth)).IsEqualTo((2026, 4, 1, 0, 0, 7));
        await Assert.That(year.Months).IsEquivalentTo([0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0], CollectionOrdering.Matching);
        await Assert.That(year.BusiestPlace).IsEqualTo(new YearPlace("West Glacier", 2));

        // A year with nothing taken.
        var empty = await Review.ReviewAsync(2024, ct);
        await Assert.That((empty.Photos, empty.Videos, empty.Trips, empty.BusiestMonth, empty.BusiestPlace)).IsEqualTo((0, 0, 0, 0, (YearPlace?)null));
    }

    [Test]
    public async Task The_Years_Trips_Are_Counted_And_The_Three_Largest_Listed_In_The_Order_They_Were_Taken(CancellationToken ct)
    {
        // Home is Butte, photographed on the most days; each trip is a day at West Glacier, far from it.
        for (var day = 1; day <= 6; day++) await AddAsync($"HOME_{day}.HEIC", new DateTime(2026, 1, day, 12, 0, 0), longitude: 1);
        foreach (var (month, photos) in new[] { (3, 10), (7, 12), (8, 15), (10, 11) })
            for (var n = 0; n < photos; n++) await AddAsync($"TRIP_{month}_{n}.HEIC", new DateTime(2026, month, 4, 9, n, 0), longitude: -1);
        for (var n = 0; n < 10; n++) await AddAsync($"OLD_{n}.HEIC", new DateTime(2025, 5, 4, 9, n, 0), longitude: -1);

        var year = await Review.ReviewAsync(2026, ct);
        await Assert.That(year.Trips).IsEqualTo(4);
        await Assert.That(year.TopTrips).IsEquivalentTo(
            [new YearTrip("West Glacier, Montana", 7, 12), new YearTrip("West Glacier, Montana", 8, 15), new YearTrip("West Glacier, Montana", 10, 11)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Photo_With_No_Date_Is_In_No_Year_And_One_Taken_Nowhere_Known_Has_No_Town(CancellationToken ct)
    {
        await _repo.AddOrUpdateAsync(new Photo { SourcePath = "/photos/IMG_7.HEIC", FileName = "IMG_7.HEIC" }, ct);
        await AddAsync("IMG_8.HEIC", new DateTime(2026, 5, 1), longitude: 2);
        _places.Setup(p => p.Locate(It.IsAny<double>(), 2)).Returns((PlaceMatch?)null);
        await _repo.AddOrUpdateAsync(new Photo { SourcePath = "/photos/IMG_9.HEIC", FileName = "IMG_9.HEIC", FileModifiedUtc = new DateTime(2026, 5, 2) }, ct);
        var year = await Review.ReviewAsync(2026, ct);
        await Assert.That((year.Photos, year.Trips, year.BusiestMonth, year.BusiestPlace)).IsEqualTo((2, 0, 5, (YearPlace?)null));
    }

    [Test]
    public async Task What_Was_Cleaned_Up_Comes_From_The_Days_Record_Or_Before_It_The_Free_Plans_Counts_With_The_Longest_Swipe_Streak(CancellationToken ct)
    {
        // Before growth step 7 only the free plan's counts were kept; from then on, every removal and what it freed.
        await _plans.AddRemovedAsync(new DateOnly(2026, 3, 1), 12, ct);
        await _plans.AddRemovedAsync(new DateOnly(2026, 12, 1), 5, ct);
        await _plans.AddCleanedAsync(new DateOnly(2026, 12, 1), 8, 4_000, ct);
        await _plans.AddCleanedAsync(new DateOnly(2026, 12, 2), 0, 1_000, ct);
        await _plans.AddRemovedAsync(new DateOnly(2025, 12, 31), 99, ct);
        foreach (var day in new[] { 1, 2, 3, 5, 6, 10, 11, 12, 13 }) await _swipes.SetDayAsync(new DateOnly(2026, 11, day), true, ct);
        await _swipes.SetDayAsync(new DateOnly(2025, 11, 1), true, ct);

        var year = await Review.ReviewAsync(2026, ct);
        await Assert.That((year.Removed, year.Freed, year.LongestStreak, year.Sessions)).IsEqualTo((20, 5_000L, 4, 9));
    }

    [Test]
    public async Task Removals_Are_Noted_With_What_They_Freed_On_Any_Plan_And_An_Undo_Takes_Them_Back_On_Their_Day(CancellationToken ct)
    {
        var parts = new PlanParts { Clock = _clock, Store = _plans };
        var removals = TestPlans.Removals(parts);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        await removals.RecordAsync(3, 9_000, await removals.TodayAsync(ct), ct);
        await removals.RecordAsync(0, 0, await removals.TodayAsync(ct), ct);
        await removals.FreedAsync(500, ct);
        await removals.FreedAsync(0, ct);
        await Assert.That((await _plans.CleanedInAsync(2026, ct)).Single()).IsEqualTo(new CleanedDay(today, 3, 9_500));
        await Assert.That(await _plans.RemovedOnAsync(today, ct)).IsEqualTo(3);

        // Pro's are noted too, though they do not count toward the limit.
        var app = new FakeAppStore();
        app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var pro = TestPlans.Removals(new PlanParts { Clock = _clock, Store = _plans, App = app });
        await pro.RecordAsync(2, 0, await pro.TodayAsync(ct), ct);
        await Assert.That(((await _plans.CleanedInAsync(2026, ct)).Single().Removed, await _plans.RemovedOnAsync(today, ct))).IsEqualTo((5, 3));

        // Undone on a later day, the free plan's count stays, and the record of its day is taken back.
        var removedAt = _clock.GetUtcNow().UtcTicks;
        _clock.Now = _clock.Now.AddDays(1);
        await removals.GiveBackAsync(2, removedAt, ct);
        await Assert.That(((await _plans.CleanedInAsync(2026, ct)).First().Removed, await _plans.RemovedOnAsync(today, ct))).IsEqualTo((3, 3));
    }

    [Test]
    public async Task The_Days_Record_Is_Kept_In_The_Database_By_Year_Never_Below_Nothing(CancellationToken ct)
    {
        using var db = new LiteDB.LiteDatabase(new MemoryStream());
        var plans = db.Plan();
        await plans.AddCleanedAsync(new DateOnly(2026, 12, 2), 4, 1_000, ct);
        await plans.AddCleanedAsync(new DateOnly(2026, 3, 1), 1, 10, ct);
        await plans.AddCleanedAsync(new DateOnly(2026, 3, 1), -3, 5, ct);
        await plans.AddCleanedAsync(new DateOnly(2025, 3, 1), 9, 9, ct);
        await plans.AddRemovedAsync(new DateOnly(2026, 3, 1), 7, ct);
        await plans.AddRemovedAsync(new DateOnly(2025, 3, 1), 2, ct);
        await Assert.That(await db.Plan().CleanedInAsync(2026, ct))
            .IsEquivalentTo([new CleanedDay(new DateOnly(2026, 3, 1), 0, 15), new CleanedDay(new DateOnly(2026, 12, 2), 4, 1_000)], CollectionOrdering.Matching);
        await Assert.That((await plans.RemovedInAsync(2026, ct)).Single()).IsEqualTo(new KeyValuePair<DateOnly, int>(new DateOnly(2026, 3, 1), 7));
    }
}

/// <summary>Year in review and the share sheet through the phone's own endpoints, as the page asks.</summary>
public sealed class YearOnAPhoneTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 1, 2, 3];
    private readonly FakeShareSheet _share = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 12, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly TestApi _phone;

    public YearOnAPhoneTests()
    {
        var places = new Mock<IPlaceNameResolver>();
        places.Setup(p => p.Locate(It.IsAny<double>(), It.IsAny<double>())).Returns(new PlaceMatch("Butte", "Montana", "US", 46, -112.5));
        _phone = new TestApi(s => s
            .AddSingleton(sp => new YearInReview(sp.GetRequiredService<IPhotoRepository>(), places.Object, sp.GetRequiredService<IPlanStore>(), sp.GetRequiredService<ISwipeStore>(),
                new Trips(sp.GetRequiredService<IPhotoRepository>(), places.Object, sp.GetRequiredService<ITripStore>()), _clock))
            .AddSingleton<IShareSheet>(_share));
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(ApiRequest request, string path, CancellationToken ct)
    {
        var answer = await _phone.SendAsync(request.FromClient(), ct, path);
        return (answer.StatusCode, answer.Text());
    }

    [Test]
    public async Task The_Year_Due_Is_Said_With_Its_Figures_And_None_Outside_December_And_January(CancellationToken ct)
    {
        await _phone.Services.GetRequiredService<IPlanStore>().AddCleanedAsync(new DateOnly(2026, 6, 1), 4, 2_000, ct);
        var (status, text) = await AskAsync(Http.Get("year"), "year", ct);
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        var year = JsonDocument.Parse(text).RootElement;
        await Assert.That((year.GetProperty("year").GetInt32(), year.GetProperty("removed").GetInt32(), year.GetProperty("freed").GetInt64(), year.GetProperty("months").GetArrayLength()))
            .IsEqualTo((2026, 4, 2_000L, 12));
        await Assert.That(year.GetProperty("busiestPlace").ValueKind).IsEqualTo(JsonValueKind.Null);

        _clock.Now = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var none = JsonDocument.Parse((await AskAsync(Http.Get("year"), "year", ct)).Text).RootElement;
        await Assert.That(none.GetProperty("year").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task A_Busiest_Place_And_The_Trips_Are_Sent_With_The_Year(CancellationToken ct)
    {
        var photo = TestPhotos.Make("IMG_1.HEIC", taken: new DateTime(2026, 7, 4));
        photo.Latitude = 46.0038;
        photo.Longitude = -112.5348;
        var repo = _phone.Services.GetRequiredService<IPhotoRepository>();
        await repo.AddOrUpdateAsync(photo, ct);
        // Ten taken some 280 km north of home make a trip.
        for (var n = 0; n < 10; n++)
        {
            var away = TestPhotos.Make($"AWAY_{n}.HEIC", taken: new DateTime(2026, 8, 1, 9, n, 0));
            away.Latitude = 48.5;
            away.Longitude = -112.5;
            await repo.AddOrUpdateAsync(away, ct);
        }
        var year = JsonDocument.Parse((await AskAsync(Http.Get("year"), "year", ct)).Text).RootElement;
        await Assert.That(year.GetProperty("busiestPlace").GetProperty("town").GetString()).IsEqualTo("Butte");
        var trip = year.GetProperty("topTrips")[0];
        await Assert.That((year.GetProperty("trips").GetInt32(), trip.GetProperty("name").GetString(), trip.GetProperty("month").GetInt32(), trip.GetProperty("photos").GetInt32()))
            .IsEqualTo((1, "Butte, Montana", 8, 10));
    }

    [Test]
    public async Task A_Card_Is_Handed_To_The_Share_Sheet_Only_As_A_Png_Of_A_Cards_Size(CancellationToken ct)
    {
        var (status, _) = await AskAsync(Http.Post("share", JsonSerializer.Serialize(new { image = Convert.ToBase64String(Png), name = "../Year in review.png" })), "share", ct);
        await Assert.That(status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(_share.Shared.Single().Name).IsEqualTo("Year in review.png");
        await AskAsync(Http.Post("share", JsonSerializer.Serialize(new { image = Convert.ToBase64String(Png), name = " " })), "share", ct);
        await Assert.That(_share.Shared[1].Name).IsEqualTo("PhotoSense.png");

        foreach (var image in new[] { "", Convert.ToBase64String([1, 2, 3, 4, 5]), "not base 64!", new string('A', 8 * 1024 * 1024 / 3 * 4 + 8) })
            await Assert.That(await AskAsync(Http.Post("share", JsonSerializer.Serialize(new { image })), "share", ct)).IsEqualTo((HttpStatusCode.BadRequest, "Send the card as a PNG."));
        await Assert.That(await AskAsync(Http.Post("share"), "share", ct)).IsEqualTo((HttpStatusCode.BadRequest, "Send the card as a PNG."));

        _share.Fail = "The share sheet could not open.";
        await Assert.That(await AskAsync(Http.Post("share", JsonSerializer.Serialize(new { image = Convert.ToBase64String(Png) })), "share", ct)).IsEqualTo((HttpStatusCode.Conflict, "The share sheet could not open."));
    }

    [Test]
    public async Task Only_A_Phone_Has_Year_In_Review_And_Shares_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        var computer = new TestApi();
        await Assert.That(await AskComputerAsync(computer, Http.Get("year"), "year", ct)).IsEqualTo((HttpStatusCode.NotFound, "Only a phone has Year in review."));
        await Assert.That(await AskComputerAsync(computer, Http.Post("share"), "share", ct)).IsEqualTo((HttpStatusCode.NotFound, "Only a phone shares cards."));
        await Assert.That((await _phone.SendAsync(Http.Get("year"), ct, "year")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task<(HttpStatusCode, string)> AskComputerAsync(TestApi computer, ApiRequest request, string path, CancellationToken ct)
    {
        var answer = await computer.SendAsync(request.FromClient(), ct, path);
        return (answer.StatusCode, answer.Text());
    }
}
