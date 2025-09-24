using Moq;
using PhotoSense.Application.Photos.Services;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class PhotoSearchServiceTests
{
    private static List<Photo> CreatePhotos() => new()
    {
        new() { SourcePath = "p1", FileName = "cat.jpg", FileSizeBytes = 1, ContentHash = "H1", PerceptualHash = "AAAA", Set = PhotoSet.Primary },
        new() { SourcePath = "p2", FileName = "dog.png", FileSizeBytes = 1, ContentHash = "H2", PerceptualHash = "AAAB", Set = PhotoSet.Primary },
        new() { SourcePath = "p3", FileName = "cat2.jpg", FileSizeBytes = 1, ContentHash = "H3", PerceptualHash = "FFFF", Set = PhotoSet.Secondary }
    };

    [Fact]
    public async Task Text_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Text: "cat"));
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Hash_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Hash: "H2"));
        Assert.Single(result.Items);
        Assert.Equal("dog.png", result.Items[0].FileName);
    }

    [Fact]
    public async Task PerceptualHash_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(PerceptualHash: "FFFF"));
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Set_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Set: nameof(PhotoSet.Secondary)));
        Assert.Single(result.Items);
        Assert.Equal(PhotoSet.Secondary, result.Items[0].Set);
    }

    [Fact]
    public async Task Paging_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        var many = new List<Photo>();
        for (int i = 0; i < 120; i++)
            many.Add(new Photo { SourcePath = $"p{i}", FileName = $"f{i}.jpg", FileSizeBytes = 1, Set = PhotoSet.Primary });
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(many);
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Page: 2, PageSize: 50));
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(120, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }
}
