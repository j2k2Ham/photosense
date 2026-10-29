using System.Runtime.InteropServices;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Domain;

public class PhotoIdentityTests
{
    [Test]
    public async Task Two_Spellings_Of_One_Path_Have_The_Same_Key()
    {
        var path = Path.Combine(Path.GetTempPath(), "album", "IMG_1.jpg");
        var roundabout = Path.Combine(Path.GetTempPath(), "album", "..", "album", "IMG_1.jpg");
        await Assert.That(PhotoPath.Key(roundabout)).IsEqualTo(PhotoPath.Key(path));
        await Assert.That(PhotoPath.Key(path + "x")).IsNotEqualTo(PhotoPath.Key(path));
        var caseBlind = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        await Assert.That(PhotoPath.Key(path) == PhotoPath.Key(path.ToUpperInvariant())).IsEqualTo(caseBlind);
        await Assert.That(PhotoPath.Comparer.Equals("a", "A")).IsEqualTo(caseBlind);
    }

    [Test]
    public async Task Saving_A_Second_Record_For_A_Path_Replaces_The_First()
    {
        var repo = new InMemoryPhotoRepository();
        var path = Path.Combine(Path.GetTempPath(), "IMG_1.jpg");
        var first = new Photo { SourcePath = path, FileName = "IMG_1.jpg", ContentHash = "A" };
        var second = new Photo { SourcePath = path, FileName = "IMG_1.jpg", ContentHash = "B" };
        await repo.AddOrUpdateAsync(first);
        var afterFirst = repo.Version;
        await repo.AddOrUpdateAsync(second);

        await Assert.That((await repo.GetAllAsync()).Single().Id).IsEqualTo(second.Id);
        await Assert.That((await repo.GetByPathAsync(path))!.Id).IsEqualTo(second.Id);
        await Assert.That(await repo.GetByPathAsync(path + ".other")).IsNull();
        await Assert.That(repo.Version > afterFirst).IsTrue();
        await repo.DeleteAsync(second.Id);
        await Assert.That(repo.Version > afterFirst + 1).IsTrue();
    }

    [Test]
    public async Task A_Moved_Photo_Is_The_Same_Photo_At_A_New_Path()
    {
        var photo = new Photo
        {
            SourcePath = Path.Combine("a", "IMG_1.HEIC"), FileName = "IMG_1.HEIC", FileSizeBytes = 9, FileModifiedUtc = DateTime.UtcNow, ScanRoot = "a",
            ContentHash = "H", PerceptualHash = "00000000000000FF", Signature = [1], Width = 4, Height = 3, Format = "HEIC", EncodedQuality = 90,
            TakenOn = TestPhotos.Shot, CameraModel = "iPhone", Latitude = 1, Longitude = 2, Set = PhotoSet.Secondary, IsKept = true
        };
        photo.AddCategory("Trip");

        var moved = photo.MovedTo(Path.Combine("b", "renamed.HEIC"));

        await Assert.That((moved.Id, moved.SourcePath, moved.FileName)).IsEqualTo((photo.Id, Path.Combine("b", "renamed.HEIC"), "renamed.HEIC"));
        await Assert.That((moved.FileSizeBytes, moved.FileModifiedUtc, moved.ScanRoot, moved.ContentHash, moved.PerceptualHash, moved.Signature)).IsEqualTo((photo.FileSizeBytes, photo.FileModifiedUtc, photo.ScanRoot, photo.ContentHash, photo.PerceptualHash, photo.Signature));
        await Assert.That((moved.Width, moved.Height, moved.Format, moved.EncodedQuality, moved.PixelCount)).IsEqualTo((4, 3, "HEIC", 90, 12L));
        await Assert.That((moved.TakenOn, moved.CameraModel, moved.Latitude, moved.Longitude, moved.Set, moved.IsKept)).IsEqualTo((photo.TakenOn, "iPhone", 1d, 2d, PhotoSet.Secondary, true));
        await Assert.That(moved.Categories).Contains("Trip");
    }

    [Test]
    public async Task Thumbnails_Default_To_A_Folder_Beside_The_Database()
    {
        var beside = new PhotoStorageOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "data", "photosense.db") };
        await Assert.That(beside.ResolveThumbnailPath()).IsEqualTo(Path.Combine(Path.GetTempPath(), "data", "photosense-thumbnails"));
        await Assert.That(new PhotoStorageOptions().ResolveThumbnailPath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense-thumbnails"));
        var chosen = new PhotoStorageOptions { ThumbnailPath = Path.Combine(Path.GetTempPath(), "thumbs") };
        await Assert.That(chosen.ResolveThumbnailPath()).IsEqualTo(Path.Combine(Path.GetTempPath(), "thumbs"));
        var named = new PhotoStorageOptions { ThumbnailPath = "previews" };
        await Assert.That(named.ResolveThumbnailPath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "previews"));
    }

    [Test]
    public async Task Data_Is_Kept_In_The_Users_Own_Folder_And_Never_Where_The_Service_Runs_From()
    {
        // The Functions host restarts itself when a folder appears beside the program, as a thumbnail cache would.
        var defaults = new PhotoStorageOptions();
        await Assert.That(defaults.ResolveDatabasePath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense.db"));
        await Assert.That(defaults.ResolveDatabaseFolder()).IsEqualTo(PhotoStorageOptions.DefaultDataFolder);
        await Assert.That(Path.IsPathFullyQualified(PhotoStorageOptions.DefaultDataFolder)).IsTrue();
        await Assert.That(Path.GetFileName(PhotoStorageOptions.DefaultDataFolder)).IsEqualTo("PhotoSense");
        await Assert.That(defaults.ResolveDatabasePath()).IsNotEqualTo(Path.Combine(Directory.GetCurrentDirectory(), "photosense.db"));
        await Assert.That(defaults.ResolveDatabasePath()).IsNotEqualTo(Path.Combine(AppContext.BaseDirectory, "photosense.db"));

        // A name alone, or a path that is not absolute, lands in that folder too.
        await Assert.That(new PhotoStorageOptions { DatabasePath = Path.Combine("library", "mine.db") }.ResolveDatabasePath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "library", "mine.db"));
        // An absolute path is taken as given.
        var chosen = Path.Combine(Path.GetTempPath(), "elsewhere", "photosense.db");
        await Assert.That(new PhotoStorageOptions { DatabasePath = chosen }.ResolveDatabasePath()).IsEqualTo(chosen);
        await Assert.That(new PhotoStorageOptions { DatabasePath = chosen }.ResolveDatabaseFolder()).IsEqualTo(Path.Combine(Path.GetTempPath(), "elsewhere"));
    }

    [Test]
    public async Task An_Account_With_No_Folder_Of_Its_Own_Keeps_Its_Data_With_The_Temporary_Files()
    {
        await Assert.That(PhotoStorageOptions.DataFolderIn(string.Empty)).IsEqualTo(Path.Combine(Path.GetTempPath(), "PhotoSense"));
        var home = Path.Combine(Path.GetTempPath(), "someone", "AppData");
        await Assert.That(PhotoStorageOptions.DataFolderIn(home)).IsEqualTo(Path.Combine(home, "PhotoSense"));
    }

    [Test]
    public async Task A_Group_Counts_Only_What_A_Removal_Would_Take()
    {
        var keeper = TestPhotos.Make("keeper.heic", size: 10);
        var group = new DuplicateGroup(keeper,
        [
            new DuplicateMember(TestPhotos.Make("a.jpg", size: 3), MatchKind.Identical, 0, 0),
            new DuplicateMember(TestPhotos.Make("b.jpg", size: 5, kept: true), MatchKind.SamePicture, 2, 0.1)
        ]);
        await Assert.That(group.Key).IsEqualTo(keeper.Id.ToString());
        await Assert.That(group.Removable.Single().FileName).IsEqualTo("a.jpg");
        await Assert.That(group.ReclaimableBytes).IsEqualTo(3);
    }

    [Test]
    public async Task Only_A_Completed_Removal_Counts_As_Success()
    {
        await Assert.That(new RemovalResult(RemovalOutcome.Removed, "held").Succeeded).IsTrue();
        foreach (var o in new[] { RemovalOutcome.NotFound, RemovalOutcome.Changed, RemovalOutcome.Failed, RemovalOutcome.LastCopy }) await Assert.That(new RemovalResult(o).Succeeded).IsFalse();
    }

    [Test]
    public async Task Case_Matters_In_A_Path_Only_Where_The_File_System_Says_So()
    {
        await Assert.That(PhotoPath.IgnoresCase(os => os == OSPlatform.Windows)).IsTrue();
        await Assert.That(PhotoPath.IgnoresCase(os => os == OSPlatform.OSX)).IsTrue();
        await Assert.That(PhotoPath.IgnoresCase(os => os == OSPlatform.Linux)).IsFalse();

        var path = Path.Combine(Path.GetTempPath(), "Album", "IMG_1.jpg");
        await Assert.That(PhotoPath.Key(path.ToUpperInvariant(), ignoreCase: true)).IsEqualTo(PhotoPath.Key(path, ignoreCase: true));
        await Assert.That(PhotoPath.Key(path.ToUpperInvariant(), ignoreCase: false)).IsNotEqualTo(PhotoPath.Key(path, ignoreCase: false));
        await Assert.That(PhotoPath.Key(path, ignoreCase: false)).IsEqualTo(Path.GetFullPath(path));
        await Assert.That(PhotoPath.ComparerFor(ignoreCase: true).Equals("a.jpg", "A.JPG")).IsTrue();
        await Assert.That(PhotoPath.ComparerFor(ignoreCase: false).Equals("a.jpg", "A.JPG")).IsFalse();
    }

    [Test]
    public async Task A_Database_Path_With_No_Folder_Puts_Thumbnails_In_The_Data_Folder()
    {
        var top = Path.GetPathRoot(Path.GetTempPath())!;
        await Assert.That(new PhotoStorageOptions { DatabasePath = top }.ResolveThumbnailPath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense-thumbnails"));
    }
}
