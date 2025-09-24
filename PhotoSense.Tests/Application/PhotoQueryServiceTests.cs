using Moq;
using PhotoSense.Application.Photos.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class PhotoQueryServiceTests
{
    [Fact]
    public async Task GetAllAsync_Forwards_To_Repo()
    {
        var photos = new List<Photo>{ new(){ SourcePath="p", FileName="f", FileSizeBytes=1, Set=PhotoSet.Primary } };
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new PhotoQueryService(repo.Object);
        var result = await svc.GetAllAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task GetAsync_Forwards_To_Repo()
    {
        var photo = new Photo{ SourcePath="p", FileName="f", FileSizeBytes=1, Set=PhotoSet.Primary };
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<PhotoSense.Domain.ValueObjects.PhotoId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photo);
        var svc = new PhotoQueryService(repo.Object);
        var result = await svc.GetAsync(photo.Id);
        Assert.NotNull(result);
    }
}
