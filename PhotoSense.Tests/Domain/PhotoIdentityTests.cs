using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class PhotoIdentityTests
{
    [Fact]
    public void Two_Spellings_Of_One_Path_Have_The_Same_Key()
    {
        var path = Path.Combine(Path.GetTempPath(), "album", "IMG_1.jpg");
        var roundabout = Path.Combine(Path.GetTempPath(), "album", "..", "album", "IMG_1.jpg");
        Assert.Equal(PhotoPath.Key(path), PhotoPath.Key(roundabout));
        Assert.NotEqual(PhotoPath.Key(path), PhotoPath.Key(path + "x"));
        var caseBlind = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.Equal(caseBlind, PhotoPath.Key(path) == PhotoPath.Key(path.ToUpperInvariant()));
        Assert.Equal(caseBlind, PhotoPath.Comparer.Equals("a", "A"));
    }

    [Fact]
    public async Task Saving_A_Second_Record_For_A_Path_Replaces_The_First()
    {
        var repo = new InMemoryPhotoRepository();
        var path = Path.Combine(Path.GetTempPath(), "IMG_1.jpg");
        var first = new Photo { SourcePath = path, FileName = "IMG_1.jpg", ContentHash = "A" };
        var second = new Photo { SourcePath = path, FileName = "IMG_1.jpg", ContentHash = "B" };
        await repo.AddOrUpdateAsync(first);
        var afterFirst = repo.Version;
        await repo.AddOrUpdateAsync(second);

        Assert.Equal(second.Id, Assert.Single(await repo.GetAllAsync()).Id);
        Assert.Equal(second.Id, (await repo.GetByPathAsync(path))!.Id);
        Assert.Null(await repo.GetByPathAsync(path + ".other"));
        Assert.True(repo.Version > afterFirst);
        await repo.DeleteAsync(second.Id);
        Assert.True(repo.Version > afterFirst + 1);
    }

    [Fact]
    public void A_Moved_Photo_Is_The_Same_Photo_At_A_New_Path()
    {
        var photo = new Photo
        {
            SourcePath = Path.Combine("a", "IMG_1.HEIC"), FileName = "IMG_1.HEIC", FileSizeBytes = 9, FileModifiedUtc = DateTime.UtcNow, ScanRoot = "a",
            ContentHash = "H", PerceptualHash = "00000000000000FF", Signature = [1], Width = 4, Height = 3, Format = "HEIC", EncodedQuality = 90,
            TakenOn = TestPhotos.Shot, CameraModel = "iPhone", Latitude = 1, Longitude = 2, Set = PhotoSet.Secondary, IsKept = true
        };
        photo.AddCategory("Trip");

        var moved = photo.MovedTo(Path.Combine("b", "renamed.HEIC"));

        Assert.Equal((photo.Id, Path.Combine("b", "renamed.HEIC"), "renamed.HEIC"), (moved.Id, moved.SourcePath, moved.FileName));
        Assert.Equal((photo.FileSizeBytes, photo.FileModifiedUtc, photo.ScanRoot, photo.ContentHash, photo.PerceptualHash, photo.Signature),
            (moved.FileSizeBytes, moved.FileModifiedUtc, moved.ScanRoot, moved.ContentHash, moved.PerceptualHash, moved.Signature));
        Assert.Equal((4, 3, "HEIC", 90, 12L), (moved.Width, moved.Height, moved.Format, moved.EncodedQuality, moved.PixelCount));
        Assert.Equal((photo.TakenOn, "iPhone", 1d, 2d, PhotoSet.Secondary, true), (moved.TakenOn, moved.CameraModel, moved.Latitude, moved.Longitude, moved.Set, moved.IsKept));
        Assert.Contains("Trip", moved.Categories);
    }

    [Fact]
    public void Thumbnails_Default_To_A_Folder_Beside_The_Database()
    {
        var beside = new PhotoStorageOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "data", "photosense.db") };
        Assert.Equal(Path.Combine(Path.GetTempPath(), "data", "photosense-thumbnails"), beside.ResolveThumbnailPath());
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "photosense-thumbnails"), new PhotoStorageOptions().ResolveThumbnailPath());
        var chosen = new PhotoStorageOptions { ThumbnailPath = Path.Combine(Path.GetTempPath(), "thumbs") };
        Assert.Equal(Path.Combine(Path.GetTempPath(), "thumbs"), chosen.ResolveThumbnailPath());
    }

    [Fact]
    public void A_Group_Counts_Only_What_A_Removal_Would_Take()
    {
        var keeper = TestPhotos.Make("keeper.heic", size: 10);
        var group = new DuplicateGroup(keeper,
        [
            new DuplicateMember(TestPhotos.Make("a.jpg", size: 3), MatchKind.Identical, 0, 0),
            new DuplicateMember(TestPhotos.Make("b.jpg", size: 5, kept: true), MatchKind.SamePicture, 2, 0.1)
        ]);
        Assert.Equal(keeper.Id.ToString(), group.Key);
        Assert.Equal("a.jpg", Assert.Single(group.Removable).FileName);
        Assert.Equal(3, group.ReclaimableBytes);
    }

    [Fact]
    public void Only_A_Completed_Removal_Counts_As_Success()
    {
        Assert.True(new RemovalResult(RemovalOutcome.Removed, "held").Succeeded);
        Assert.All(new[] { RemovalOutcome.NotFound, RemovalOutcome.Changed, RemovalOutcome.Failed }, o => Assert.False(new RemovalResult(o).Succeeded));
    }
}
