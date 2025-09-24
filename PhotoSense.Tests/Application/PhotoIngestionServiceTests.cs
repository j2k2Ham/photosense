using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Core.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Application;

public class PhotoIngestionServiceTests
{
    [Fact]
    public async Task ReturnsFalse_When_File_Missing()
    {
        var repo = new Mock<IPhotoRepository>();
        var hash = new Mock<IImageHashingService>();
        var meta = new Mock<IPhotoMetadataExtractor>();
        var svc = new PhotoIngestionService(repo.Object, hash.Object, meta.Object);
        var result = await svc.IngestAsync("nonexistent.xyz", PhotoSet.Primary);
        Assert.False(result);
        repo.Verify(r => r.AddOrUpdateAsync(It.IsAny<PhotoSense.Core.Domain.Entities.Photo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ingests_File_Successfully()
    {
        var repo = new Mock<IPhotoRepository>();
        var hash = new Mock<IImageHashingService>();
        hash.Setup(h => h.ComputeHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("HASH");
        hash.Setup(h => h.ComputePerceptualHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("PHASH");
        var meta = new Mock<IPhotoMetadataExtractor>();
        var svc = new PhotoIngestionService(repo.Object, hash.Object, meta.Object);
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, new byte[]{1,2,3});
        try
        {
            var ok = await svc.IngestAsync(path, PhotoSet.Primary);
            Assert.True(ok);
            repo.Verify(r => r.AddOrUpdateAsync(It.Is<Photo>(p => p.ContentHash == "HASH" && p.PerceptualHash == "PHASH"), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { File.Delete(path); }
    }
}
