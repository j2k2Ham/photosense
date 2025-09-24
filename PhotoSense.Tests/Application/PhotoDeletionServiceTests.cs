using Moq;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.ValueObjects;
using PhotoSense.Infrastructure.Deletion;
using Xunit;

namespace PhotoSense.Tests.Application;

public class PhotoDeletionServiceTests
{
    [Fact]
    public async Task Delete_Ignores_Missing_Photo()
    {
        var repo = new Mock<IPhotoRepository>();
        var publisher = new Mock<IIntegrationEventPublisher>();
        var svc = new FileSystemPhotoDeletionService(repo.Object, publisher.Object);
        await svc.DeleteAsync(new PhotoId(Guid.NewGuid()), false);
        repo.Verify(r => r.DeleteAsync(It.IsAny<PhotoId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Removes_And_Publishes()
    {
        var repo = new Mock<IPhotoRepository>();
        var publisher = new Mock<IIntegrationEventPublisher>();
        var photo = new Photo { SourcePath = "x", FileName = "f", FileSizeBytes = 1, Set = PhotoSet.Primary };
        repo.Setup(r => r.GetAsync(photo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(photo);
        var svc = new FileSystemPhotoDeletionService(repo.Object, publisher.Object);
        await svc.DeleteAsync(photo.Id, false);
        repo.Verify(r => r.DeleteAsync(photo.Id, It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
