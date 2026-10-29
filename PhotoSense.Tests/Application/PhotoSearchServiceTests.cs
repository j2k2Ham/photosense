using Moq;
using PhotoSense.Application.Photos.Services;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Application;

public class PhotoSearchServiceTests
{
    private static List<Photo> CreatePhotos() => new()
    {
        new() { SourcePath = "p1", FileName = "cat.jpg", FileSizeBytes = 1, ContentHash = "H1", PerceptualHash = "AAAA", Set = PhotoSet.Primary },
        new() { SourcePath = "p2", FileName = "dog.png", FileSizeBytes = 1, ContentHash = "H2", PerceptualHash = "AAAB", Set = PhotoSet.Primary },
        new() { SourcePath = "p3", FileName = "cat2.jpg", FileSizeBytes = 1, ContentHash = "H3", PerceptualHash = "FFFF", Set = PhotoSet.Secondary }
    };

    [Test]
    public async Task Text_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Text: "cat"));
        await Assert.That(result.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Hash_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Hash: "H2"));
        await Assert.That(result.Items).HasSingleItem();
        await Assert.That(result.Items[0].FileName).IsEqualTo("dog.png");
    }

    [Test]
    public async Task PerceptualHash_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(PerceptualHash: "FFFF"));
        await Assert.That(result.Items).HasSingleItem();
    }

    [Test]
    public async Task Set_Filter_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePhotos());
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Set: nameof(PhotoSet.Secondary)));
        await Assert.That(result.Items).HasSingleItem();
        await Assert.That(result.Items[0].Set).IsEqualTo(PhotoSet.Secondary);
    }

    [Test]
    public async Task Paging_Works()
    {
        var repo = new Mock<IPhotoRepository>();
        var many = new List<Photo>();
        for (int i = 0; i < 120; i++)
            many.Add(new Photo { SourcePath = $"p{i}", FileName = $"f{i}.jpg", FileSizeBytes = 1, Set = PhotoSet.Primary });
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(many);
        var svc = new PhotoSearchService(repo.Object);
        var result = await svc.SearchAsync(new PhotoSearchQuery(Page: 2, PageSize: 50));
        await Assert.That(result.Items.Count).IsEqualTo(50);
        await Assert.That(result.TotalCount).IsEqualTo(120);
        await Assert.That(result.TotalPages).IsEqualTo(3);
    }
}
