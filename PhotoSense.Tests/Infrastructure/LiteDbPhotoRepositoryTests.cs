using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public sealed class LiteDbPhotoRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, Path.ChangeExtension(_db, null) + "-log.db" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Fact]
    public async Task RoundTrip_Persists_And_Maps_Categories()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var photo = new Photo{ SourcePath="x", FileName="y.jpg", FileSizeBytes=1, Set=PhotoSet.Secondary, ContentHash="ABC", PerceptualHash="FFFF" };
        photo.AddCategory("Nature");
        await repo.AddOrUpdateAsync(photo);
        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Contains("Nature", all[0].Categories);
        var byId = await repo.GetAsync(photo.Id);
        Assert.NotNull(byId);
        var byHash = await repo.GetByHashAsync("ABC");
        Assert.Single(byHash);
        await repo.DeleteAsync(photo.Id);
        var empty = await repo.GetAllAsync();
        Assert.Empty(empty);
    }

    [Fact]
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

        var listed = Assert.Single(await repo.GetAllAsync());
        // The id handed out in a listing must find the same record again, or nothing can be kept or removed.
        Assert.Equal(photo.Id, listed.Id);
        var read = await repo.GetAsync(listed.Id);
        Assert.NotNull(read);
        Assert.Equal(photo.Id, read!.Id);
        Assert.Equal(modified, read.FileModifiedUtc); // to the tick: an unchanged file must compare equal
        Assert.Equal(DateTimeKind.Utc, read.FileModifiedUtc!.Value.Kind);
        Assert.Equal(TestPhotos.Shot, read.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, read.TakenOn!.Value.Kind); // not shifted into this computer's zone
        Assert.Equal((photo.ScanRoot, "00000000000000FF", 4032, 3024, "HEIC", 94), (read.ScanRoot, read.PerceptualHash, read.Width, read.Height, read.Format, read.EncodedQuality));
        Assert.Equal(new byte[] { 1, 2, 3 }, read.Signature);
        Assert.Equal(("iPhone", 35.2, -80.8, PhotoSet.Primary, true), (read.CameraModel, read.Latitude, read.Longitude, read.Set, read.IsKept));
    }

    [Fact]
    public async Task A_File_Has_One_Record_However_It_Is_Saved()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var path = Path.Combine(Path.GetTempPath(), "IMG_0001.JPG");
        var first = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "A" };
        var again = new Photo { SourcePath = path, FileName = "IMG_0001.JPG", ContentHash = "B" };
        await repo.AddOrUpdateAsync(first);
        await repo.AddOrUpdateAsync(again);

        var only = Assert.Single(await repo.GetAllAsync());
        Assert.Equal(again.Id, only.Id);
        Assert.Equal(again.Id, (await repo.GetByPathAsync(path))!.Id);
        Assert.Null(await repo.GetByPathAsync(Path.Combine(Path.GetTempPath(), "other.jpg")));
        if (OperatingSystem.IsWindows())
            Assert.Equal(again.Id, (await repo.GetByPathAsync(path.ToLowerInvariant()))!.Id);
    }

    [Fact]
    public async Task Version_Changes_With_Every_Write()
    {
        using var repo = new LiteDbPhotoRepository(_db);
        var start = repo.Version;
        var photo = new Photo { SourcePath = "a", FileName = "a" };
        await repo.AddOrUpdateAsync(photo);
        var afterAdd = repo.Version;
        await repo.DeleteAsync(photo.Id);
        Assert.True(start < afterAdd && afterAdd < repo.Version);
    }

    [Fact]
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
        Assert.Equal(2, (await repo.GetAllAsync()).Count);
        Assert.NotNull(await repo.GetByPathAsync(path));
        repo.Dispose(); // a shared database stays open for its other users
        Assert.Equal(2, shared.GetCollection("photos").Count());
    }
}
