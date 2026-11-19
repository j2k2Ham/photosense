using LiteDB;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>A phone's backup locations, what each holds and the Safe to delete rule, kept in the database.</summary>
public class LiteDbLocationStoreTests
{
    private static BackupLocation Archive(bool on = true) => new("s3-1", LocationKind.AmazonS3, "Archive", on, "AKIA", new Dictionary<string, string> { ["bucket"] = "photos" },
        TestClock.Day, TestClock.Day.AddHours(1), null, "Down");

    [Test]
    public async Task A_Location_Is_Kept_Whole_And_Changed_In_Place(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = db.Locations();
        await Assert.That(await store.ListAsync(ct)).IsEmpty();
        await store.SaveAsync(Archive(), ct);
        var kept = (await db.Locations().ListAsync(ct)).Single();
        await Assert.That((kept.Id, kept.Kind, kept.Name, kept.On, kept.Account, kept.Settings["bucket"])).IsEqualTo(("s3-1", LocationKind.AmazonS3, "Archive", true, (string?)"AKIA", "photos"));
        await Assert.That((kept.LastUsed, kept.LastBackup, kept.LastChecked, kept.Problem)).IsEqualTo(((DateTimeOffset?)TestClock.Day, (DateTimeOffset?)TestClock.Day.AddHours(1), (DateTimeOffset?)null, (string?)"Down"));
        await store.SaveAsync(Archive(on: false), ct);
        await Assert.That((await store.ListAsync(ct)).Single().On).IsFalse();
    }

    [Test]
    public async Task What_A_Location_Holds_Is_Kept_By_Checksum_With_Its_Own_Checksum_And_Forgotten_With_It(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = db.Locations();
        await store.SaveAsync(Archive(), ct);
        await store.KeepCopyAsync("s3-1", "aaa", new LocationCopy("IMG_1.JPG", new RemoteChecksum(ChecksumKind.Md5, "m")), ct);
        await store.KeepCopyAsync("s3-1", "bbb", new LocationCopy("IMG_2.JPG", null), ct);
        await store.KeepCopyAsync("other", "aaa", new LocationCopy("IMG_1.JPG", null), ct);
        var copies = await store.CopiesAsync("s3-1", ct);
        await Assert.That(copies["aaa"]).IsEqualTo(new LocationCopy("IMG_1.JPG", new RemoteChecksum(ChecksumKind.Md5, "m")));
        await Assert.That(copies["bbb"]).IsEqualTo(new LocationCopy("IMG_2.JPG", null));
        await store.ForgetCopyAsync("s3-1", "bbb", ct);
        await Assert.That((await store.CopiesAsync("s3-1", ct)).Keys).IsEquivalentTo(["aaa"]);
        await store.RemoveAsync("s3-1", ct);
        await Assert.That(await store.ListAsync(ct)).IsEmpty();
        await Assert.That(await store.CopiesAsync("s3-1", ct)).IsEmpty();
        await Assert.That((await store.CopiesAsync("other", ct)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_Copy_Counts_On_Any_Location_Until_The_Person_Chooses_Two(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = db.Locations();
        await Assert.That(await store.CountAtAsync(ct)).IsEqualTo(1);
        await store.SaveCountAtAsync(2, ct);
        await Assert.That(await db.Locations().CountAtAsync(ct)).IsEqualTo(2);
    }
}
