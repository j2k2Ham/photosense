using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>Scans' records kept in the database, one to a file.</summary>
public sealed class LiteDbPhotoRepositoryTests
{
    [Test]
    public async Task RoundTrip_Persists_And_Maps_Categories(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var photo = new Photo{ SourcePath="x", FileName="y.jpg", FileSizeBytes=1, Set=PhotoSet.Secondary, ContentHash="ABC", PerceptualHash="FFFF" };
        photo.AddCategory("Nature");
        await repo.AddOrUpdateAsync(photo, ct);
        var all = await repo.GetAllAsync(ct);
        await Assert.That(all).HasSingleItem();
        await Assert.That(all[0].Categories).Contains("Nature");
        var byId = await repo.GetAsync(photo.Id, ct);
        await Assert.That(byId).IsNotNull();
        var byHash = await repo.GetByHashAsync("ABC", ct);
        await Assert.That(byHash).HasSingleItem();
        await repo.DeleteAsync(photo.Id, ct);
        var empty = await repo.GetAllAsync(ct);
        await Assert.That(empty).IsEmpty();
    }

    [Test]
    public async Task A_Photo_Read_Back_Has_The_Id_And_Details_It_Was_Saved_With(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var modified = new DateTime(2024, 4, 7, 21, 35, 59, DateTimeKind.Utc).AddTicks(1234567);
        var photo = new Photo
        {
            SourcePath = Path.Combine(Path.GetTempPath(), "IMG_0001.HEIC"), FileName = "IMG_0001.HEIC", FileSizeBytes = 42, FileModifiedUtc = modified,
            ScanRoot = Path.GetTempPath(), ContentHash = "ABC", PerceptualHash = "00000000000000FF", Signature = [1, 2, 3], Width = 4032, Height = 3024,
            Format = "HEIC", EncodedQuality = 94, TakenOn = TestPhotos.Shot, CameraModel = "iPhone", Latitude = 35.2, Longitude = -80.8, Set = PhotoSet.Primary, IsKept = true
        };
        await repo.AddOrUpdateAsync(photo, ct);

        var listed = (await repo.GetAllAsync(ct)).Single();
        // The id handed out in a listing must find the same record again, or nothing can be kept or removed.
        await Assert.That(listed.Id).IsEqualTo(photo.Id);
        var read = await repo.GetAsync(listed.Id, ct);
        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Id).IsEqualTo(photo.Id);
        await Assert.That(read.FileModifiedUtc).IsEqualTo(modified); // to the tick: an unchanged file must compare equal
        await Assert.That(read.FileModifiedUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(read.TakenOn).IsEqualTo(TestPhotos.Shot);
        await Assert.That(read.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified); // not shifted into this computer's zone
        await Assert.That((read.ScanRoot, read.PerceptualHash, read.Width, read.Height, read.Format, read.EncodedQuality)).IsEqualTo((photo.ScanRoot, "00000000000000FF", 4032, 3024, "HEIC", 94));
        await Assert.That(read.Signature).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That((read.CameraModel, read.Latitude, read.Longitude, read.Set, read.IsKept)).IsEqualTo(("iPhone", 35.2, -80.8, PhotoSet.Primary, true));
    }

    [Test]
    public async Task A_File_Has_One_Record_However_It_Is_Saved(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var path = Path.Combine(Path.GetTempPath(), "IMG_0001.JPG");
        var first = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "A" };
        var again = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "B" };
        await repo.AddOrUpdateAsync(first, ct);
        await repo.AddOrUpdateAsync(again, ct);

        var only = (await repo.GetAllAsync(ct)).Single();
        await Assert.That(only.Id).IsEqualTo(again.Id);
        await Assert.That((await repo.GetByPathAsync(path, ct))!.Id).IsEqualTo(again.Id);
        await Assert.That(await repo.GetByPathAsync(Path.Combine(Path.GetTempPath(), "other.jpg"), ct)).IsNull();
        if (OperatingSystem.IsWindows())
            await Assert.That((await repo.GetByPathAsync(path.ToLowerInvariant(), ct))!.Id).IsEqualTo(again.Id);
    }

    [Test]
    public async Task Several_Records_Are_Saved_And_Forgotten_As_One(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var folder = Path.GetTempPath();
        var a = new Photo { SourcePath = Path.Combine(folder, "a.jpg"), FileName = "a.jpg", ContentHash = "A" };
        var b = new Photo { SourcePath = Path.Combine(folder, "b.jpg"), FileName = "b.jpg", ContentHash = "B" };
        var start = repo.Version;
        await repo.SaveAllAsync([a, b], ct);
        await repo.SaveAllAsync([a.MovedTo(Path.Combine(folder, "Butte", "a.jpg")), b.MovedTo(Path.Combine(folder, "Butte", "b.jpg"))], ct);
        await Assert.That((await repo.GetAllAsync(ct)).Select(p => p.SourcePath).Order()).IsEquivalentTo([Path.Combine(folder, "Butte", "a.jpg"), Path.Combine(folder, "Butte", "b.jpg")], CollectionOrdering.Matching);

        await repo.DeleteAllAsync([a.Id, b.Id], ct);
        await Assert.That(await repo.GetAllAsync(ct)).IsEmpty();
        await repo.SaveAllAsync([], ct);
        await repo.DeleteAllAsync([], ct);
        await Assert.That(repo.Version - start).IsEqualTo(5);
    }

    [Test]
    public async Task Records_Saved_As_One_Are_All_Kept_Or_None_And_A_Files_Record_Is_Never_Lost_Between_Two(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var folder = Path.GetTempPath();
        var a = new Photo { SourcePath = Path.Combine(folder, "a.jpg"), FileName = "a.jpg", ContentHash = "A" };
        var b = new Photo { SourcePath = Path.Combine(folder, "b.jpg"), FileName = "b.jpg", ContentHash = "B" };
        await repo.SaveAllAsync([a, b], ct);
        var version = repo.Version;

        // The second record cannot be kept (the database refuses an index key over 1,023 bytes), after the first was moved
        // and after b's own record, of the same file, was taken away to make room for it.
        var unkeepable = new Photo { SourcePath = b.SourcePath, FileName = "b.jpg", ContentHash = new string('F', 2000) };
        await Assert.ThrowsAsync<LiteException>(() => repo.SaveAllAsync([a.MovedTo(Path.Combine(folder, "Butte", "a.jpg")), unkeepable], ct));
        var kept = await repo.GetAllAsync(ct);
        await Assert.That(kept.Select(p => (p.Id, p.SourcePath)).OrderBy(p => p.SourcePath)).IsEquivalentTo([(a.Id, a.SourcePath), (b.Id, b.SourcePath)], CollectionOrdering.Matching);
        await Assert.That(repo.Version).IsEqualTo(version);

        // One record alone is saved the same way: the file keeps the record it had.
        await Assert.ThrowsAsync<LiteException>(() => repo.AddOrUpdateAsync(unkeepable, ct));
        await Assert.That((await repo.GetByPathAsync(b.SourcePath, ct))!.Id).IsEqualTo(b.Id);
        // And the database takes other writes afterwards.
        await repo.DeleteAsync(a.Id, ct);
        await Assert.That((await repo.GetAllAsync(ct)).Single().Id).IsEqualTo(b.Id);
    }

    [Test]
    public async Task Version_Changes_With_Every_Write(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        var start = repo.Version;
        var photo = new Photo { SourcePath = "a", FileName = "a" };
        await repo.AddOrUpdateAsync(photo, ct);
        var afterAdd = repo.Version;
        await repo.DeleteAsync(photo.Id, ct);
        await Assert.That(start < afterAdd && afterAdd < repo.Version).IsTrue();
    }

    [Test]
    public async Task Opening_An_Older_Database_Leaves_One_Record_Per_File(CancellationToken ct)
    {
        // Before records were keyed by path, every scan added a record for every file.
        var path = Path.Combine(Path.GetTempPath(), "IMG_0001.JPG");
        using var shared = new LiteDatabase(new MemoryStream());
        {
            var photos = shared.GetCollection("photos");
            foreach (var _ in Enumerable.Range(0, 3))
                photos.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["SourcePath"] = path, ["FileName"] = "IMG_0001.JPG", ["ContentHash"] = "A", ["Set"] = 1, ["Categories"] = new BsonArray() });
            photos.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["SourcePath"] = path + "2", ["FileName"] = "IMG_0002.JPG", ["ContentHash"] = "A", ["Set"] = 1, ["Categories"] = new BsonArray() });
        }

        var repo = shared.Photos();
        await Assert.That((await repo.GetAllAsync(ct)).Count).IsEqualTo(2);
        await Assert.That(await repo.GetByPathAsync(path, ct)).IsNotNull();
        await Assert.That(shared.GetCollection("photos").Count()).IsEqualTo(2);
    }

    [Test]
    public async Task An_Unknown_Id_Or_Path_Finds_Nothing(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        await Assert.That(await repo.GetAsync(PhotoId.New(), ct)).IsNull();
        await Assert.That(await repo.GetByPathAsync(Path.Combine(Path.GetTempPath(), "never-scanned.jpg"), ct)).IsNull();
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Counts_Them(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var repo = db.Photos();
        await Assert.That(await repo.ClearAsync(ct)).IsEqualTo(0);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "a.jpg"), FileName = "a.jpg", ContentHash = "A" }, ct);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "b.jpg"), FileName = "b.jpg", ContentHash = "B" }, ct);
        var version = repo.Version;

        await Assert.That(await repo.ClearAsync(ct)).IsEqualTo(2);

        await Assert.That(await repo.GetAllAsync(ct)).IsEmpty();
        await Assert.That(await repo.GetByHashAsync("A", ct)).IsEmpty();
        await Assert.That(repo.Version).IsNotEqualTo(version);

        // And it can be filled again afterwards.
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "c.jpg"), FileName = "c.jpg", ContentHash = "C" }, ct);
        await Assert.That(await repo.GetAllAsync(ct)).HasSingleItem();
    }
}
