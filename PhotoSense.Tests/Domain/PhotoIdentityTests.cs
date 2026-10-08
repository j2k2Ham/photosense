using System.Runtime.InteropServices;
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
        Assert.Equal(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense-thumbnails"), new PhotoStorageOptions().ResolveThumbnailPath());
        var chosen = new PhotoStorageOptions { ThumbnailPath = Path.Combine(Path.GetTempPath(), "thumbs") };
        Assert.Equal(Path.Combine(Path.GetTempPath(), "thumbs"), chosen.ResolveThumbnailPath());
        var named = new PhotoStorageOptions { ThumbnailPath = "previews" };
        Assert.Equal(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "previews"), named.ResolveThumbnailPath());
    }

    [Fact]
    public void Data_Is_Kept_In_The_Users_Own_Folder_And_Never_Where_The_Service_Runs_From()
    {
        // The Functions host restarts itself when a folder appears beside the program, as a thumbnail cache would.
        var defaults = new PhotoStorageOptions();
        Assert.Equal(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense.db"), defaults.ResolveDatabasePath());
        Assert.Equal(PhotoStorageOptions.DefaultDataFolder, defaults.ResolveDatabaseFolder());
        Assert.True(Path.IsPathFullyQualified(PhotoStorageOptions.DefaultDataFolder));
        Assert.Equal("PhotoSense", Path.GetFileName(PhotoStorageOptions.DefaultDataFolder));
        Assert.NotEqual(Path.Combine(Directory.GetCurrentDirectory(), "photosense.db"), defaults.ResolveDatabasePath());
        Assert.NotEqual(Path.Combine(AppContext.BaseDirectory, "photosense.db"), defaults.ResolveDatabasePath());

        // A name alone, or a path that is not absolute, lands in that folder too.
        Assert.Equal(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "library", "mine.db"), new PhotoStorageOptions { DatabasePath = Path.Combine("library", "mine.db") }.ResolveDatabasePath());
        // An absolute path is taken as given.
        var chosen = Path.Combine(Path.GetTempPath(), "elsewhere", "photosense.db");
        Assert.Equal(chosen, new PhotoStorageOptions { DatabasePath = chosen }.ResolveDatabasePath());
        Assert.Equal(Path.Combine(Path.GetTempPath(), "elsewhere"), new PhotoStorageOptions { DatabasePath = chosen }.ResolveDatabaseFolder());
    }

    [Fact]
    public void An_Account_With_No_Folder_Of_Its_Own_Keeps_Its_Data_With_The_Temporary_Files()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "PhotoSense"), PhotoStorageOptions.DataFolderIn(string.Empty));
        var home = Path.Combine(Path.GetTempPath(), "someone", "AppData");
        Assert.Equal(Path.Combine(home, "PhotoSense"), PhotoStorageOptions.DataFolderIn(home));
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
        Assert.All(new[] { RemovalOutcome.NotFound, RemovalOutcome.Changed, RemovalOutcome.Failed, RemovalOutcome.LastCopy }, o => Assert.False(new RemovalResult(o).Succeeded));
    }

    [Fact]
    public void Case_Matters_In_A_Path_Only_Where_The_File_System_Says_So()
    {
        Assert.True(PhotoPath.IgnoresCase(os => os == OSPlatform.Windows));
        Assert.True(PhotoPath.IgnoresCase(os => os == OSPlatform.OSX));
        Assert.False(PhotoPath.IgnoresCase(os => os == OSPlatform.Linux));

        var path = Path.Combine(Path.GetTempPath(), "Album", "IMG_1.jpg");
        Assert.Equal(PhotoPath.Key(path, ignoreCase: true), PhotoPath.Key(path.ToUpperInvariant(), ignoreCase: true));
        Assert.NotEqual(PhotoPath.Key(path, ignoreCase: false), PhotoPath.Key(path.ToUpperInvariant(), ignoreCase: false));
        Assert.Equal(Path.GetFullPath(path), PhotoPath.Key(path, ignoreCase: false));
        Assert.True(PhotoPath.ComparerFor(ignoreCase: true).Equals("a.jpg", "A.JPG"));
        Assert.False(PhotoPath.ComparerFor(ignoreCase: false).Equals("a.jpg", "A.JPG"));
    }

    [Fact]
    public void A_Database_Path_With_No_Folder_Puts_Thumbnails_In_The_Data_Folder()
    {
        var top = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense-thumbnails"), new PhotoStorageOptions { DatabasePath = top }.ResolveThumbnailPath());
    }
}
