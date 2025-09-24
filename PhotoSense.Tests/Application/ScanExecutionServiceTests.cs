using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Application;

public class ScanExecutionServiceTests
{
    [Fact]
    public async Task ProcessAsync_AddsPhotos_And_UpdatesProgress()
    {
        var repo = new Mock<IPhotoRepository>();
        var hash = new Mock<IImageHashingService>();
        hash.Setup(h => h.ComputeHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("H");
        hash.Setup(h => h.ComputePerceptualHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("PH");
        var meta = new Mock<IPhotoMetadataExtractor>();
        var progress = new Mock<IScanProgressStore>();
        var svc = new ScanExecutionService(repo.Object, hash.Object, meta.Object, progress.Object);
        var tmp1 = Path.GetTempFileName();
        var tmp2 = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tmp1, new byte[]{1});
        await File.WriteAllBytesAsync(tmp2, new byte[]{2});
        try
        {
            await svc.ProcessAsync(new []{ tmp1, tmp2 }, PhotoSet.Primary, "inst");
            repo.Verify(r => r.AddOrUpdateAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            progress.Verify(p => p.IncrementProcessed("inst", true), Times.Exactly(2));
        }
        finally { File.Delete(tmp1); File.Delete(tmp2); }
    }

    [Fact]
    public async Task ProcessAsync_SecondarySet_IncrementsWithFalse()
    {
        var repo = new Mock<IPhotoRepository>();
        var hash = new Mock<IImageHashingService>();
        hash.Setup(h => h.ComputeHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("H");
        hash.Setup(h => h.ComputePerceptualHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync("PH");
        var meta = new Mock<IPhotoMetadataExtractor>();
        var progress = new Mock<IScanProgressStore>();
        var svc = new ScanExecutionService(repo.Object, hash.Object, meta.Object, progress.Object);
        var tmp = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tmp, new byte[]{3});
        try
        {
            await svc.ProcessAsync(new []{ tmp }, PhotoSet.Secondary, "inst2");
            progress.Verify(p => p.IncrementProcessed("inst2", false), Times.Once);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task CountAsync_UsesEnumerator()
    {
        var repo = new Mock<IPhotoRepository>();
        var hash = new Mock<IImageHashingService>();
        var meta = new Mock<IPhotoMetadataExtractor>();
        var progress = new Mock<IScanProgressStore>();
        var svc = new ScanExecutionService(repo.Object, hash.Object, meta.Object, progress.Object);
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "a.jpg"), new byte[]{1});
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "b.png"), new byte[]{1});
            var count = await svc.CountAsync(dir.FullName, false);
            Assert.Equal(2, count);
        }
        finally { dir.Delete(true); }
    }
}
