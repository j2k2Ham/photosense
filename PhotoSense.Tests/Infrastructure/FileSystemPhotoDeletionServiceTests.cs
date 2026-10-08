using Moq;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public sealed class FileSystemPhotoDeletionServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-delete-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly Mock<IIntegrationEventPublisher> _publisher = new();
    private readonly FileSystemPhotoDeletionService _service;

    public FileSystemPhotoDeletionServiceTests() => _service = new FileSystemPhotoDeletionService(_repo, _publisher.Object, new CompanionFileFinder(_ => null));

    public void Dispose() => _root.Delete(true);

    private async Task<Photo> AddAsync(string relativePath, string? scanRoot = "", string content = "picture")
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        var info = new FileInfo(path);
        var photo = new Photo
        {
            SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc,
            ScanRoot = scanRoot == "" ? _root.FullName : scanRoot, ContentHash = content
        };
        await _repo.AddOrUpdateAsync(photo);
        return photo;
    }

    private string Held(params string[] parts) => Path.Combine([_root.FullName, PhotoStorageOptions.RemovedFolderName, .. parts]);

    [Test]
    public async Task Moves_The_File_Aside_Keeping_Its_Place_Under_The_Scanned_Folder()
    {
        var photo = await AddAsync(Path.Combine("2024", "trip", "IMG_1.jpg"));

        var result = await _service.DeleteAsync(photo.Id, deleteFile: true);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsEqualTo(Held("2024", "trip", "IMG_1.jpg"));
        await Assert.That(File.Exists(photo.SourcePath)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(result.HeldAt!)).IsEqualTo("picture");
        await Assert.That(await _repo.GetAsync(photo.Id)).IsNull();
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Does_Not_Overwrite_A_File_Already_Held_Under_The_Same_Name()
    {
        var first = await AddAsync("IMG_1.jpg", content: "first");
        await Assert.That((await _service.DeleteAsync(first.Id, true)).HeldAt).IsEqualTo(Held("IMG_1.jpg"));
        var second = await AddAsync("IMG_1.jpg", content: "second");
        var third = (await _service.DeleteAsync(second.Id, true)).HeldAt;

        await Assert.That(third).IsEqualTo(Held("IMG_1 (2).jpg"));
        await Assert.That(await File.ReadAllTextAsync(Held("IMG_1.jpg"))).IsEqualTo("first");
        await Assert.That(await File.ReadAllTextAsync(third!)).IsEqualTo("second");
    }

    [Test]
    [Arguments(null)]
    [Arguments("elsewhere")]
    public async Task Holds_The_File_Beside_Itself_When_Its_Scanned_Folder_Is_Unknown_Or_Not_Above_It(string? scanRoot)
    {
        var photo = await AddAsync(Path.Combine("sub", "IMG_1.jpg"), scanRoot is null ? null : Path.Combine(_root.FullName, scanRoot));
        var result = await _service.DeleteAsync(photo.Id, true);
        await Assert.That(result.HeldAt).IsEqualTo(Path.Combine(_root.FullName, "sub", PhotoStorageOptions.RemovedFolderName, "IMG_1.jpg"));
    }

    [Test]
    public async Task Leaves_A_File_That_Changed_Since_It_Was_Scanned()
    {
        var resized = await AddAsync("resized.jpg");
        await File.WriteAllTextAsync(resized.SourcePath, "a different length now");
        var touched = await AddAsync("touched.jpg");
        File.SetLastWriteTimeUtc(touched.SourcePath, touched.FileModifiedUtc!.Value.AddMinutes(5));

        await Assert.That((await _service.DeleteAsync(resized.Id, true)).Outcome).IsEqualTo(RemovalOutcome.Changed);
        await Assert.That((await _service.DeleteAsync(touched.Id, true)).Outcome).IsEqualTo(RemovalOutcome.Changed);
        await Assert.That(File.Exists(resized.SourcePath) && File.Exists(touched.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(resized.Id)).IsNotNull();
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Keeps_The_Record_When_The_File_Cannot_Be_Moved()
    {
        var photo = await AddAsync("IMG_1.jpg");
        RemovalResult result;
        using (new FileStream(photo.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await _service.DeleteAsync(photo.Id, true);

        await Assert.That(result.Outcome).IsEqualTo(RemovalOutcome.Failed);
        await Assert.That(string.IsNullOrEmpty(result.Error)).IsFalse();
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(photo.Id)).IsNotNull();
    }

    [Test]
    public async Task A_Copy_Goes_While_One_Of_The_Files_It_Copies_Is_Still_There()
    {
        var gone = await AddAsync("IMG_1.jpg");
        var original = await AddAsync("IMG_2.jpg");
        var copy = await AddAsync("IMG_3.jpg");
        File.Delete(gone.SourcePath);

        var result = await _service.DeleteAsync(copy.Id, true, [gone, original]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsEqualTo(Held("IMG_3.jpg"));
        await Assert.That(File.Exists(original.SourcePath)).IsTrue();
    }

    [Test]
    public async Task A_Copy_Is_Left_Alone_When_Nothing_It_Copies_Is_Left_As_It_Was_Scanned()
    {
        var gone = await AddAsync("IMG_1.jpg");
        var edited = await AddAsync("IMG_2.jpg");
        var copy = await AddAsync("IMG_3.jpg");
        File.Delete(gone.SourcePath);
        await File.WriteAllTextAsync(edited.SourcePath, "no longer the picture that was scanned");

        await Assert.That((await _service.DeleteAsync(copy.Id, true, [gone, edited])).Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That((await _service.DeleteAsync(copy.Id, true, [])).Outcome).IsEqualTo(RemovalOutcome.LastCopy);

        await Assert.That(await File.ReadAllTextAsync(copy.SourcePath)).IsEqualTo("picture");
        await Assert.That(Directory.Exists(Held())).IsFalse();
        await Assert.That(await _repo.GetAsync(copy.Id)).IsNotNull();
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task A_File_Recorded_Under_Two_Paths_Is_Put_Back_Rather_Than_Removed_As_A_Copy_Of_Itself()
    {
        // One file, reached through the folder itself and through a link to that folder.
        var original = await AddAsync(Path.Combine("photos", "IMG_1.jpg"), Path.Combine(_root.FullName, "photos"));
        var alias = Path.Combine(_root.FullName, "alias");
        using var link = TestFiles.LinkFolder(alias, Path.Combine(_root.FullName, "photos"));
        var same = new Photo
        {
            SourcePath = Path.Combine(alias, "IMG_1.jpg"), FileName = "IMG_1.jpg", FileSizeBytes = original.FileSizeBytes,
            FileModifiedUtc = original.FileModifiedUtc, ScanRoot = alias, ContentHash = original.ContentHash
        };
        await _repo.AddOrUpdateAsync(same);

        var result = await _service.DeleteAsync(same.Id, true, [original]);

        await Assert.That(result.Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(await File.ReadAllTextAsync(original.SourcePath)).IsEqualTo("picture");
        await Assert.That(Directory.GetFiles(Path.Combine(_root.FullName, "photos", PhotoStorageOptions.RemovedFolderName))).IsEmpty();
        await Assert.That(await _repo.GetAsync(same.Id)).IsNotNull();
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Forgetting_A_Copy_Without_Deleting_It_Asks_Nothing_Of_The_Files_It_Copies()
    {
        var copy = await AddAsync("IMG_3.jpg");
        await Assert.That((await _service.DeleteAsync(copy.Id, deleteFile: false, [])).Succeeded).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Forgetting_Without_Deleting_Leaves_The_File_Where_It_Is()
    {
        var photo = await AddAsync("IMG_1.jpg");
        var result = await _service.DeleteAsync(photo.Id, deleteFile: false);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsNull();
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(photo.Id)).IsNull();
    }

    [Test]
    public async Task A_Record_Whose_File_Is_Already_Gone_Is_Simply_Forgotten()
    {
        var photo = await AddAsync("IMG_1.jpg");
        File.Delete(photo.SourcePath);
        var result = await _service.DeleteAsync(photo.Id, true);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsNull();
        await Assert.That(await _repo.GetAsync(photo.Id)).IsNull();
    }

    [Test]
    public async Task An_Unknown_Id_Is_Reported()
        => await Assert.That((await _service.DeleteAsync(PhotoSense.Domain.ValueObjects.PhotoId.New(), true)).Outcome).IsEqualTo(RemovalOutcome.NotFound);

    [Test]
    public async Task A_Record_Without_A_Modified_Time_Is_Checked_By_Size_Alone()
    {
        var path = Path.Combine(_root.FullName, "IMG_7.jpg");
        await File.WriteAllTextAsync(path, "picture");
        var photo = new Photo { SourcePath = path, FileName = "IMG_7.jpg", FileSizeBytes = new FileInfo(path).Length, ScanRoot = _root.FullName, ContentHash = "picture" };
        await _repo.AddOrUpdateAsync(photo);

        await Assert.That((await _service.DeleteAsync(photo.Id, deleteFile: true)).Succeeded).IsTrue();
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(File.Exists(Held("IMG_7.jpg"))).IsTrue();
    }
}
