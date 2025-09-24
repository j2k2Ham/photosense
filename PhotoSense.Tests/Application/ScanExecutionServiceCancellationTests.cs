using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Application;

public class ScanExecutionServiceCancellationTests
{
    [Fact]
    public async Task ProcessAsync_Respects_Cancellation()
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
        using var cts = new CancellationTokenSource();
        try
        {
            // Cancel after first file processed
            var task = Task.Run(async () =>
            {
                try { await svc.ProcessAsync(new[]{ tmp1, tmp2 }, PhotoSet.Primary, "inst-cancel", cts.Token); }
                catch (OperationCanceledException) { }
            });
            // Allow first iteration to start
            await Task.Delay(10);
            cts.Cancel();
            await task;
            // At least one increment (first file) but not both.
            progress.Verify(p => p.IncrementProcessed("inst-cancel", true), Times.AtLeastOnce);
            progress.Verify(p => p.IncrementProcessed("inst-cancel", true), Times.AtMost(2));
        }
        finally { File.Delete(tmp1); File.Delete(tmp2); }
    }
}
