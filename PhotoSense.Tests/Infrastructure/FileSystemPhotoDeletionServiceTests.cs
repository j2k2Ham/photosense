using Moq;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

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

    [Fact]
    public async Task Moves_The_File_Aside_Keeping_Its_Place_Under_The_Scanned_Folder()
    {
        var photo = await AddAsync(Path.Combine("2024", "trip", "IMG_1.jpg"));

        var result = await _service.DeleteAsync(photo.Id, deleteFile: true);

        Assert.True(result.Succeeded);
        Assert.Equal(Held("2024", "trip", "IMG_1.jpg"), result.HeldAt);
        Assert.False(File.Exists(photo.SourcePath));
        Assert.Equal("picture", await File.ReadAllTextAsync(result.HeldAt!));
        Assert.Null(await _repo.GetAsync(photo.Id));
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Does_Not_Overwrite_A_File_Already_Held_Under_The_Same_Name()
    {
        var first = await AddAsync("IMG_1.jpg", content: "first");
        Assert.Equal(Held("IMG_1.jpg"), (await _service.DeleteAsync(first.Id, true)).HeldAt);
        var second = await AddAsync("IMG_1.jpg", content: "second");
        var third = (await _service.DeleteAsync(second.Id, true)).HeldAt;

        Assert.Equal(Held("IMG_1 (2).jpg"), third);
        Assert.Equal("first", await File.ReadAllTextAsync(Held("IMG_1.jpg")));
        Assert.Equal("second", await File.ReadAllTextAsync(third!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("elsewhere")]
    public async Task Holds_The_File_Beside_Itself_When_Its_Scanned_Folder_Is_Unknown_Or_Not_Above_It(string? scanRoot)
    {
        var photo = await AddAsync(Path.Combine("sub", "IMG_1.jpg"), scanRoot is null ? null : Path.Combine(_root.FullName, scanRoot));
        var result = await _service.DeleteAsync(photo.Id, true);
        Assert.Equal(Path.Combine(_root.FullName, "sub", PhotoStorageOptions.RemovedFolderName, "IMG_1.jpg"), result.HeldAt);
    }

    [Fact]
    public async Task Leaves_A_File_That_Changed_Since_It_Was_Scanned()
    {
        var resized = await AddAsync("resized.jpg");
        await File.WriteAllTextAsync(resized.SourcePath, "a different length now");
        var touched = await AddAsync("touched.jpg");
        File.SetLastWriteTimeUtc(touched.SourcePath, touched.FileModifiedUtc!.Value.AddMinutes(5));

        Assert.Equal(RemovalOutcome.Changed, (await _service.DeleteAsync(resized.Id, true)).Outcome);
        Assert.Equal(RemovalOutcome.Changed, (await _service.DeleteAsync(touched.Id, true)).Outcome);
        Assert.True(File.Exists(resized.SourcePath) && File.Exists(touched.SourcePath));
        Assert.NotNull(await _repo.GetAsync(resized.Id));
        _publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Keeps_The_Record_When_The_File_Cannot_Be_Moved()
    {
        var photo = await AddAsync("IMG_1.jpg");
        RemovalResult result;
        using (new FileStream(photo.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await _service.DeleteAsync(photo.Id, true);

        Assert.Equal(RemovalOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.True(File.Exists(photo.SourcePath));
        Assert.NotNull(await _repo.GetAsync(photo.Id));
    }

    [Fact]
    public async Task Forgetting_Without_Deleting_Leaves_The_File_Where_It_Is()
    {
        var photo = await AddAsync("IMG_1.jpg");
        var result = await _service.DeleteAsync(photo.Id, deleteFile: false);
        Assert.True(result.Succeeded);
        Assert.Null(result.HeldAt);
        Assert.True(File.Exists(photo.SourcePath));
        Assert.Null(await _repo.GetAsync(photo.Id));
    }

    [Fact]
    public async Task A_Record_Whose_File_Is_Already_Gone_Is_Simply_Forgotten()
    {
        var photo = await AddAsync("IMG_1.jpg");
        File.Delete(photo.SourcePath);
        var result = await _service.DeleteAsync(photo.Id, true);
        Assert.True(result.Succeeded);
        Assert.Null(result.HeldAt);
        Assert.Null(await _repo.GetAsync(photo.Id));
    }

    [Fact]
    public async Task An_Unknown_Id_Is_Reported()
        => Assert.Equal(RemovalOutcome.NotFound, (await _service.DeleteAsync(PhotoSense.Domain.ValueObjects.PhotoId.New(), true)).Outcome);
}
