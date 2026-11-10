using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Transfer;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Server;

/// <summary>What the program on a computer registers, and on what.</summary>
public sealed class PhotoSenseServicesTests : IDisposable
{
    private readonly ServerFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Test]
    public async Task Every_Service_And_Endpoint_Can_Be_Built(CancellationToken ct)
    {
        foreach (var type in new[]
        {
            typeof(IPhotoRepository), typeof(IAuditRepository), typeof(IScanHistory), typeof(IImageHashingService), typeof(IImageAnalyzer), typeof(IThumbnailStore),
            typeof(IPhotoMetadataExtractor), typeof(PhotoRanking), typeof(IDuplicateAnalysisService), typeof(IDuplicateRemovalService), typeof(IPlaceNameResolver),
            typeof(ICompanionFileFinder), typeof(ISystemViewer), typeof(IFolderBrowser), typeof(FileChanges),
            typeof(IScanProgressStore), typeof(IScanLogSink), typeof(IScanExecutionService), typeof(IValidateOptions<PhotoStorageOptions>),
            typeof(IOrganizeBatchStore), typeof(IPairingStore), typeof(IMediaDetailsStore), typeof(PhoneReceiving),
            // The addresses are made with every endpoint behind them.
            typeof(ScanRunner), typeof(PhotoSenseRoutes), typeof(IPhotoAccess),
        })
            await Assert.That(_f.Services.GetRequiredService(type)).IsNotNull();

        // About says it is the program for a computer, with the notices built into it.
        var program = _f.Services.GetRequiredService<ProgramInfo>();
        await Assert.That(program.Program).IsEqualTo("server");
        await Assert.That(program.Notices()).StartsWith("Third-party notices for PhotoSense for Windows and Mac");
        await Assert.That(_f.Services.GetRequiredService<ProductInfo>()).IsSameReferenceAs(ProductInfo.Bundled);

        // The configured preference reaches the ranking: the JPEG is the keeper.
        var ranking = _f.Services.GetRequiredService<PhotoRanking>();
        var heic = TestPhotos.Make("a.HEIC");
        var jpeg = TestPhotos.Make("a.JPG", format: "JPEG");
        await Assert.That(new[] { heic, jpeg }.OrderBy(p => p, ranking.BestFirst).First()).IsSameReferenceAs(jpeg);
        await Assert.That(_f.Services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()).IsEqualTo(_f.ThumbnailPath);
    }
}
