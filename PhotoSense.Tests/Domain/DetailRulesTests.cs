using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

/// <summary>The rules of Fix dates and places and of naming by date: wrong dates, the nearest place, names, and EXIF's way of writing them.</summary>
public class DetailRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0);

    [Test]
    public async Task A_Date_Is_Wrong_On_A_Clocks_First_Day_Or_In_The_Future(CancellationToken ct)
    {
        await Assert.That(DetailRules.IsWrongDate(new DateTime(2000, 1, 1, 9, 30, 0), Now)).IsTrue();
        await Assert.That(DetailRules.IsWrongDate(new DateTime(1970, 1, 1), Now)).IsTrue();
        await Assert.That(DetailRules.IsWrongDate(new DateTime(1980, 1, 1), Now)).IsTrue();
        await Assert.That(DetailRules.IsWrongDate(Now.AddDays(2), Now)).IsTrue();
        // A New Year's Day of another year, Jan 2 of a clock's year, and a day ahead (another time zone) are kept.
        await Assert.That(DetailRules.IsWrongDate(new DateTime(2024, 1, 1), Now)).IsFalse();
        await Assert.That(DetailRules.IsWrongDate(new DateTime(2000, 1, 2), Now)).IsFalse();
        await Assert.That(DetailRules.IsWrongDate(new DateTime(2000, 2, 1), Now)).IsFalse();
        await Assert.That(DetailRules.IsWrongDate(Now.AddHours(20), Now)).IsFalse();
    }

    [Test]
    public async Task A_Photo_With_No_Place_Gets_That_Of_The_One_Nearest_In_Time_Within_30_Minutes(CancellationToken ct)
    {
        var at = (int minutes) => new DateTime(2026, 7, 18, 9, 0, 0).AddMinutes(minutes);
        IReadOnlyList<(string Name, DateTime When)> placed = [("A", at(0)), ("B", at(60)), ("C", at(200))];
        var placeless = new[] { ("before A", at(-20)), ("near B", at(50)), ("between", at(130)), ("after C", at(229)), ("far after", at(240)) };
        var found = DetailRules.NearestPlaced(placeless, placed, p => p.Item2);
        await Assert.That(found.Select(f => (f.Photo.Item1, f.Nearest.Item1))).IsEquivalentTo([("before A", "A"), ("near B", "B"), ("after C", "C")], CollectionOrdering.Matching);
        await Assert.That(DetailRules.NearestPlaced(placeless, [], p => p.Item2).Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_Copy_Is_Named_By_Its_Date_And_Place_And_Numbered_When_The_Name_Is_Taken(CancellationToken ct)
    {
        var taken = new DateTime(2026, 3, 2, 14, 31, 7);
        await Assert.That(DetailRules.NameByDate(taken, null, ".HEIC")).IsEqualTo("2026-03-02 14.31.07.HEIC");
        await Assert.That(DetailRules.NameByDate(taken, " ", ".HEIC")).IsEqualTo("2026-03-02 14.31.07.HEIC");
        await Assert.That(DetailRules.NameByDate(taken, " Big Sky ", ".mov")).IsEqualTo("2026-03-02 14.31.07 Big Sky.mov");
        HashSet<string> there = ["2026-03-02 14.31.07.HEIC", "2026-03-02 14.31.07-2.HEIC"];
        await Assert.That(DetailRules.Numbered("2026-03-02 14.31.07.HEIC", there.Contains)).IsEqualTo("2026-03-02 14.31.07-3.HEIC");
        await Assert.That(DetailRules.Numbered("free.HEIC", there.Contains)).IsEqualTo("free.HEIC");
    }

    [Test]
    public async Task Dates_And_Places_Are_Written_As_EXIF_Has_Them(CancellationToken ct)
    {
        await Assert.That(DetailRules.ExifDate(new DateTime(2026, 3, 2, 14, 31, 7))).IsEqualTo("2026:03:02 14:31:07");
        await Assert.That(DetailRules.ExifDegrees(45.6797, "N", "S")).IsEqualTo(("45/1,40/1,4692/100", "N"));
        await Assert.That(DetailRules.ExifDegrees(-111.0386, "E", "W")).IsEqualTo(("111/1,2/1,1896/100", "W"));
    }
}
