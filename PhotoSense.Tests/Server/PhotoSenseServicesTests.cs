using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PhotoSense.Api;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

public sealed class PhotoSenseServicesTests : IDisposable
{
    private readonly ServerFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Test]
    public async Task Every_Service_And_Endpoint_Can_Be_Built()
    {
        foreach (var type in new[]
        {
            typeof(LiteDatabase), typeof(IPhotoRepository), typeof(IAuditRepository), typeof(IScanHistory), typeof(IImageHashingService), typeof(IImageAnalyzer), typeof(IThumbnailStore),
            typeof(IPhotoMetadataExtractor), typeof(PhotoRanking), typeof(IDuplicateAnalysisService), typeof(IDuplicateRemovalService), typeof(IPlaceNameResolver),
            typeof(PhotoDtoMapper), typeof(ScanGroupingFacade), typeof(IScanRequestPublisher), typeof(IOutboxStore), typeof(IIntegrationEventPublisher),
            typeof(ICompanionFileFinder), typeof(ISystemViewer), typeof(IFolderBrowser), typeof(FileChanges), typeof(IPhotoSearchService),
            typeof(IScanProgressStore), typeof(IScanLogSink), typeof(IScanExecutionService), typeof(IValidateOptions<PhotoStorageOptions>),
            typeof(ScanRunner), typeof(PhotoSenseRoutes), typeof(HealthEndpoints), typeof(FoldersEndpoints), typeof(PhotosEndpoints), typeof(DuplicateGroupsEndpoints),
            typeof(ScanStatusEndpoints), typeof(ScanStartEndpoints), typeof(ScanResetEndpoints), typeof(ScanProgressEndpoints), typeof(ScanLogsEndpoints),
            typeof(OrganizeEndpoints), typeof(RemovedEndpoints), typeof(OutboxEndpoints),
        })
            await Assert.That(_f.Services.GetRequiredService(type)).IsNotNull();

        // The configured preference reaches the ranking: the JPEG is the keeper.
        var ranking = _f.Services.GetRequiredService<PhotoRanking>();
        var heic = TestPhotos.Make("a.HEIC");
        var jpeg = TestPhotos.Make("a.JPG", format: "JPEG");
        await Assert.That(new[] { heic, jpeg }.OrderBy(p => p, ranking.BestFirst).First()).IsSameReferenceAs(jpeg);
        await Assert.That(_f.Services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()).IsEqualTo(_f.ThumbnailPath);
        // The folder for the database is made on first use.
        await Assert.That(File.Exists(_f.DatabasePath)).IsTrue();
        // Changes a stop cut short are put back when the program starts.
        await Assert.That(_f.Services.GetServices<IHostedService>().OfType<PutBackUnfinishedChanges>()).HasSingleItem();
    }
}
