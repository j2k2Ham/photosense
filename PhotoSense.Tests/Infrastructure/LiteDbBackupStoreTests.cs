using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>What a computer keeps of the phones backing up to it, and what a phone keeps of its backups, in the database.</summary>
public class LiteDbBackupStoreTests
{
    private static readonly DateTimeOffset Day = TestClock.Day;

    [Test]
    public async Task A_Computer_Keeps_Each_Phone_In_The_Order_It_Was_Paired_With_What_It_Holds_And_What_Happened(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = db.Backups();
        var pixel = new BackupPhone("pixel", "Sam's Pixel 9", "Android phone", @"C:\Pictures\PhotoSense\Sam's Pixel 9", false, Day.AddDays(1), null, null);
        var iphone = new BackupPhone("iphone", "Alex's iPhone", null, @"C:\Pictures\PhotoSense\Alex's iPhone", true, Day, Day.AddHours(1), Day.AddHours(2), 5998, 28_100_000_000);
        await store.SavePhoneAsync(pixel, ct);
        await store.SavePhoneAsync(iphone, ct);
        await Assert.That(await db.Backups().PhonesAsync(ct)).IsEquivalentTo([iphone, pixel], CollectionOrdering.Matching);
        await store.SavePhoneAsync(pixel with { Paused = true }, ct);
        await Assert.That((await store.PhonesAsync(ct))[1].Paused).IsTrue();

        var held = new HeldFile("iphone", "aa11", @"C:\Pictures\PhotoSense\Alex's iPhone\IMG_0001.HEIC", 2_400_000, new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), Day);
        await store.HoldAsync(held, ct);
        await store.HoldAsync(held with { Sha256 = "bb22", Path = @"C:\x\IMG_0002.HEIC" }, ct);
        await store.HoldAsync(held with { PhoneId = "pixel" }, ct);
        await store.HoldAsync(held with { Size = 9 }, ct);
        await Assert.That((await db.Backups().HeldAsync("iphone", ct)).OrderBy(f => f.Sha256)).IsEquivalentTo([held with { Size = 9 }, held with { Sha256 = "bb22", Path = @"C:\x\IMG_0002.HEIC" }], CollectionOrdering.Matching);
        await store.ForgetAsync("iphone", ["aa11", "never"], ct);
        await Assert.That((await store.HeldAsync("iphone", ct)).Select(f => f.Sha256)).IsEquivalentTo(["bb22"], CollectionOrdering.Matching);
        await Assert.That((await store.HeldAsync("pixel", ct)).Count).IsEqualTo(1);

        await store.NoteAsync(new BackupEvent("iphone", Day, "Paired with this computer"), ct);
        await store.NoteAsync(new BackupEvent("iphone", Day.AddHours(1), "Receiving 380 items"), ct);
        await store.NoteAsync(new BackupEvent("iphone", Day.AddHours(1), "380 items backed up and checked"), ct);
        await store.NoteAsync(new BackupEvent("pixel", Day, "Paired with this computer"), ct);
        // The latest first; of two at one moment, the one noted last.
        await Assert.That((await db.Backups().EventsAsync("iphone", 2, ct)).Select(e => e.Text)).IsEquivalentTo(["380 items backed up and checked", "Receiving 380 items"], CollectionOrdering.Matching);
        await Assert.That((await store.EventsAsync("iphone", 9, ct)).Last()).IsEqualTo(new BackupEvent("iphone", Day, "Paired with this computer"));
    }

    [Test]
    public async Task A_Phone_Keeps_Its_Computer_Its_Settings_And_Which_Checksums_The_Computer_Holds(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var ledger = db.Ledger();
        await Assert.That(await ledger.ComputerAsync(ct)).IsNull();
        await Assert.That(await ledger.SettingsAsync(ct)).IsEqualTo(BackupSettings.Default);
        var studio = new BackupComputer("studio", "Studio iMac", "Pictures/PhotoSense/Alex's iPhone", null, null);
        await ledger.SaveComputerAsync(studio, ct);
        await Assert.That(await db.Ledger().ComputerAsync(ct)).IsEqualTo(studio);
        await ledger.KeepOnComputerAsync(["aa", "bb"], all: false, ct);
        await ledger.KeepOnComputerAsync(["cc"], all: false, ct);
        await Assert.That((await db.Ledger().OnComputerAsync(ct)).Order()).IsEquivalentTo(["aa", "bb", "cc"], CollectionOrdering.Matching);
        await ledger.KeepOnComputerAsync(["bb", "dd"], all: true, ct);
        await Assert.That((await ledger.OnComputerAsync(ct)).Order()).IsEquivalentTo(["bb", "dd"], CollectionOrdering.Matching);

        // The same computer, checked again: what it holds stays known.
        var checkedNow = studio with { LastChecked = Day, LastBackup = Day.AddMinutes(4) };
        await ledger.SaveComputerAsync(checkedNow, ct);
        await Assert.That(await db.Ledger().ComputerAsync(ct)).IsEqualTo(checkedNow);
        await Assert.That((await ledger.OnComputerAsync(ct)).Count).IsEqualTo(2);
        // Another computer holds none of it.
        await ledger.SaveComputerAsync(new BackupComputer("pc", "Office PC", "Pictures/PhotoSense", null, null), ct);
        await Assert.That((await ledger.OnComputerAsync(ct)).Count).IsEqualTo(0);

        var settings = new BackupSettings(true, false, false, false, true);
        await ledger.SaveSettingsAsync(settings, ct);
        await Assert.That(await db.Ledger().SettingsAsync(ct)).IsEqualTo(settings);
    }

    [Test]
    public async Task A_Files_Checksum_Holds_Only_While_Its_Size_And_Time_Stay_The_Same(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var ledger = db.Ledger();
        var at = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        await ledger.KeepChecksumAsync("/Photo Library/IMG_0001.HEIC", 2_400_000, at, "aa11", ct);
        await Assert.That(await db.Ledger().ChecksumAsync("/Photo Library/IMG_0001.HEIC", 2_400_000, at, ct)).IsEqualTo("aa11");
        await Assert.That(await ledger.ChecksumAsync("/Photo Library/IMG_0001.HEIC", 2_400_001, at, ct)).IsNull();
        await Assert.That(await ledger.ChecksumAsync("/Photo Library/IMG_0001.HEIC", 2_400_000, at.AddSeconds(1), ct)).IsNull();
        await Assert.That(await ledger.ChecksumAsync("/Photo Library/IMG_0002.HEIC", 2_400_000, at, ct)).IsNull();
    }

    [Test]
    public async Task What_The_Computer_Holds_Is_Replaced_Whole_Or_Not_At_All(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var ledger = db.Ledger();
        await ledger.KeepOnComputerAsync(["aa", "bb"], all: false, ct);
        // A key over 1,023 bytes fails as it is written: the old ones are not lost with it.
        await Assert.That(() => ledger.KeepOnComputerAsync(["cc", new string('x', 1100)], all: true, ct)).Throws<LiteException>();
        await Assert.That((await ledger.OnComputerAsync(ct)).Order()).IsEquivalentTo(["aa", "bb"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Each_Store_Stops_Before_It_Starts_When_Its_Caller_Has_Gone(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var gone = new CancellationToken(canceled: true);
        var store = db.Backups();
        var ledger = db.Ledger();
        var phone = new BackupPhone("p", "P", null, "f", false, Day, null, null);
        var file = new HeldFile("p", "aa", "f", 1, DateTime.UnixEpoch, Day);
        Func<Task>[] asked =
        [
            () => store.PhonesAsync(gone), () => store.SavePhoneAsync(phone, gone), () => store.HeldAsync("p", gone), () => store.HoldAsync(file, gone),
            () => store.ForgetAsync("p", ["aa"], gone), () => store.NoteAsync(new BackupEvent("p", Day, "x"), gone), () => store.EventsAsync("p", 1, gone),
            () => ledger.ComputerAsync(gone), () => ledger.SaveComputerAsync(new BackupComputer("c", "C", "f", null, null), gone), () => ledger.SettingsAsync(gone),
            () => ledger.SaveSettingsAsync(BackupSettings.Default, gone), () => ledger.OnComputerAsync(gone), () => ledger.KeepOnComputerAsync(["aa"], true, gone),
            () => ledger.ChecksumAsync("f", 1, DateTime.UnixEpoch, gone), () => ledger.KeepChecksumAsync("f", 1, DateTime.UnixEpoch, "aa", gone),
            () => ledger.FoldersMadeAsync(gone), () => ledger.KeepFolderMadeAsync("2026-07 Glacier", gone),
            () => db.TripHome().HomeAsync(gone), () => db.TripHome().SaveHomeAsync(null, gone),
        ];
        foreach (var ask in asked) await Assert.That(ask).Throws<OperationCanceledException>();
        await Assert.That((await store.PhonesAsync(ct)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_Phones_Trip_Folders_Are_Kept_On_The_Computer_And_On_The_Phone_Until_Another_Computer(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        await db.Backups().SavePhoneAsync(new BackupPhone("p", "P", null, "f", false, Day, null, null, TripFolders: ["/t/2026-07 Glacier", "/t/2025-06 Butte"]), ct);
        await db.Backups().SavePhoneAsync(new BackupPhone("q", "Q", null, "g", false, Day.AddDays(1), null, null), ct);
        var phones = await db.Backups().PhonesAsync(ct);
        await Assert.That(phones[0].TripFolders).IsEquivalentTo(["/t/2026-07 Glacier", "/t/2025-06 Butte"], CollectionOrdering.Matching);
        await Assert.That(phones[1].TripFolders).IsNull();

        var ledger = db.Ledger();
        await ledger.SaveComputerAsync(new BackupComputer("c1", "Studio iMac", "f", null, null, "Pictures/PhotoSense"), ct);
        await Assert.That((await db.Ledger().ComputerAsync(ct))!.Trips).IsEqualTo("Pictures/PhotoSense");
        await ledger.KeepFolderMadeAsync("2026-07 Glacier", ct);
        await ledger.KeepFolderMadeAsync("2026-07 Glacier", ct);
        await Assert.That(await db.Ledger().FoldersMadeAsync(ct)).IsEquivalentTo(["2026-07 Glacier"]);
        // The same computer renamed keeps them; another has none of them.
        await ledger.SaveComputerAsync(new BackupComputer("c1", "Office iMac", "f", null, null), ct);
        await Assert.That((await ledger.FoldersMadeAsync(ct)).Count).IsEqualTo(1);
        await ledger.SaveComputerAsync(new BackupComputer("c2", "Laptop", "f", null, null), ct);
        await Assert.That((await ledger.FoldersMadeAsync(ct)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task The_Home_Chosen_For_Trips_Is_Kept_Until_PhotoSense_Is_Let_Work_It_Out_Again(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        await Assert.That(await db.TripHome().HomeAsync(ct)).IsNull();
        var bozeman = new HomeTown("Bozeman", "Montana", "US", 45.6797, -111.0386);
        await db.TripHome().SaveHomeAsync(bozeman, ct);
        await Assert.That(await db.TripHome().HomeAsync(ct)).IsEqualTo(bozeman);
        await db.TripHome().SaveHomeAsync(null, ct);
        await Assert.That(await db.TripHome().HomeAsync(ct)).IsNull();
    }
}
