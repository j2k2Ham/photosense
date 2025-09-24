using Moq;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Application;

public class FileSystemPhotoDeletionServicePhysicalTests
{
    [Fact]
    public async Task Deletes_Physical_File_When_Requested()
    {
        var repo = new Mock<IPhotoRepository>();
        var publisher = new Mock<IIntegrationEventPublisher>();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "test");
        var photo = new Photo { SourcePath = path, FileName = Path.GetFileName(path), FileSizeBytes = 4, Set = PhotoSet.Primary };
        repo.Setup(r => r.GetAsync(photo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(photo);
        var svc = new FileSystemPhotoDeletionService(repo.Object, publisher.Object);
        await svc.DeleteAsync(photo.Id, true);
        Assert.False(File.Exists(path));
        repo.Verify(r => r.DeleteAsync(photo.Id, It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
