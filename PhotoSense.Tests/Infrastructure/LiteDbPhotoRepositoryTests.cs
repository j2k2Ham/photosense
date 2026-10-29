using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public sealed class LiteDbPhotoRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, Path.ChangeExtension(_db, null) + "-log.db" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Test]
    public async Task RoundTrip_Persists_And_Maps_Categories()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var photo = new Photo{ SourcePath="x", FileName="y.jpg", FileSizeBytes=1, Set=PhotoSet.Secondary, ContentHash="ABC", PerceptualHash="FFFF" };
        photo.AddCategory("Nature");
        await repo.AddOrUpdateAsync(photo);
        var all = await repo.GetAllAsync();
        await Assert.That(all).HasSingleItem();
        await Assert.That(all[0].Categories).Contains("Nature");
        var byId = await repo.GetAsync(photo.Id);
        await Assert.That(byId).IsNotNull();
        var byHash = await repo.GetByHashAsync("ABC");
        await Assert.That(byHash).HasSingleItem();
        await repo.DeleteAsync(photo.Id);
        var empty = await repo.GetAllAsync();
        await Assert.That(empty).IsEmpty();
    }

    [Test]
    public async Task A_Photo_Read_Back_Has_The_Id_And_Details_It_Was_Saved_With()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var modified = new DateTime(2024, 4, 7, 21, 35, 59, DateTimeKind.Utc).AddTicks(1234567);
        var photo = new Photo
        {
            SourcePath = Path.Combine(Path.GetTempPath(), "IMG_0001.HEIC"), FileName = "IMG_0001.HEIC", FileSizeBytes = 42, FileModifiedUtc = modified,
            ScanRoot = Path.GetTempPath(), ContentHash = "ABC", PerceptualHash = "00000000000000FF", Signature = [1, 2, 3], Width = 4032, Height = 3024,
            Format = "HEIC", EncodedQuality = 94, TakenOn = TestPhotos.Shot, CameraModel = "iPhone", Latitude = 35.2, Longitude = -80.8, Set = PhotoSet.Primary, IsKept = true
        };
        await repo.AddOrUpdateAsync(photo);

        var listed = (await repo.GetAllAsync()).Single();
        // The id handed out in a listing must find the same record again, or nothing can be kept or removed.
        await Assert.That(listed.Id).IsEqualTo(photo.Id);
        var read = await repo.GetAsync(listed.Id);
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
    public async Task A_File_Has_One_Record_However_It_Is_Saved()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var path = Path.Combine(Path.GetTempPath(), "IMG_0001.JPG");
        var first = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "A" };
        var again = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "B" };
        await repo.AddOrUpdateAsync(first);
        await repo.AddOrUpdateAsync(again);

        var only = (await repo.GetAllAsync()).Single();
        await Assert.That(only.Id).IsEqualTo(again.Id);
        await Assert.That((await repo.GetByPathAsync(path))!.Id).IsEqualTo(again.Id);
        await Assert.That(await repo.GetByPathAsync(Path.Combine(Path.GetTempPath(), "other.jpg"))).IsNull();
        if (OperatingSystem.IsWindows())
            await Assert.That((await repo.GetByPathAsync(path.ToLowerInvariant()))!.Id).IsEqualTo(again.Id);
    }

    [Test]
    public async Task Version_Changes_With_Every_Write()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var start = repo.Version;
        var photo = new Photo { SourcePath = "a", FileName = "a" };
        await repo.AddOrUpdateAsync(photo);
        var afterAdd = repo.Version;
        await repo.DeleteAsync(photo.Id);
        await Assert.That(start < afterAdd && afterAdd < repo.Version).IsTrue();
    }

    [Test]
    public async Task Opening_An_Older_Database_Leaves_One_Record_Per_File()
    {
        // Before records were keyed by path, every scan added a record for every file.
        var path = Path.Combine(Path.GetTempPath(), "IMG_0001.JPG");
        using (var db = new LiteDatabase(_db))
        {
            var photos = db.GetCollection("photos");
            foreach (var _ in Enumerable.Range(0, 3))
                photos.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["SourcePath"] = path, ["FileName"] = "IMG_0001.JPG", ["ContentHash"] = "A", ["Set"] = 1, ["Categories"] = new BsonArray() });
            photos.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["SourcePath"] = path + "2", ["FileName"] = "IMG_0002.JPG", ["ContentHash"] = "A", ["Set"] = 1, ["Categories"] = new BsonArray() });
        }

        using var shared = new LiteDatabase(_db);
        var repo = new LiteDbPhotoRepository(shared);
        await Assert.That((await repo.GetAllAsync()).Count).IsEqualTo(2);
        await Assert.That(await repo.GetByPathAsync(path)).IsNotNull();
        repo.Dispose(); // a shared database stays open for its other users
        await Assert.That(shared.GetCollection("photos").Count()).IsEqualTo(2);
    }

    [Test]
    public async Task An_Unknown_Id_Or_Path_Finds_Nothing()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        await Assert.That(await repo.GetAsync(PhotoId.New())).IsNull();
        await Assert.That(await repo.GetByPathAsync(Path.Combine(Path.GetTempPath(), "never-scanned.jpg"))).IsNull();
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Counts_Them()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        await Assert.That(await repo.ClearAsync()).IsEqualTo(0);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "a.jpg"), FileName = "a.jpg", ContentHash = "A" });
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "b.jpg"), FileName = "b.jpg", ContentHash = "B" });
        var version = repo.Version;

        await Assert.That(await repo.ClearAsync()).IsEqualTo(2);

        await Assert.That(await repo.GetAllAsync()).IsEmpty();
        await Assert.That(await repo.GetByHashAsync("A")).IsEmpty();
        await Assert.That(repo.Version).IsNotEqualTo(version);

        // And it can be filled again afterwards.
        await repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "c.jpg"), FileName = "c.jpg", ContentHash = "C" });
        await Assert.That(await repo.GetAllAsync()).HasSingleItem();
    }
}
