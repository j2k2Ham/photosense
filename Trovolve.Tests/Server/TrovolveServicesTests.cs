using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Application.Changes;
using Trovolve.Application.Scanning;
using Trovolve.Application.Transfer;
using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Application.Scanning.Services;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;

namespace Trovolve.Tests.Server;

/// <summary>What the program on a computer registers, and on what.</summary>
public sealed class TrovolveServicesTests : IDisposable
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
            typeof(ScanRunner), typeof(TrovolveRoutes), typeof(IPhotoAccess),
        })
            await Assert.That(_f.Services.GetRequiredService(type)).IsNotNull();

        // About says it is the program for a computer, with the notices built into it.
        var program = _f.Services.GetRequiredService<ProgramInfo>();
        await Assert.That(program.Program).IsEqualTo("server");
        await Assert.That(program.Notices()).StartsWith("Third-party notices for Trovolve for Windows and Mac");
        await Assert.That(_f.Services.GetRequiredService<ProductInfo>()).IsSameReferenceAs(ProductInfo.Bundled);

        // The configured preference reaches the ranking: the JPEG is the keeper.
        var ranking = _f.Services.GetRequiredService<PhotoRanking>();
        var heic = TestPhotos.Make("a.HEIC");
        var jpeg = TestPhotos.Make("a.JPG", format: "JPEG");
        await Assert.That(new[] { heic, jpeg }.OrderBy(p => p, ranking.BestFirst).First()).IsSameReferenceAs(jpeg);
        await Assert.That(_f.Services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()).IsEqualTo(_f.ThumbnailPath);
    }

    [Test]
    public async Task Move_To_Recycle_Bin_Uses_The_Computers_Own_Bin_Unless_Turned_Off_As_The_End_To_End_Tests_Do(CancellationToken ct)
    {
        // This computer's temporary folder is on a drive of its own: it has a bin on Windows and a Mac, none on Linux.
        var temp = Path.GetTempPath();
        await Assert.That(_f.Services.GetRequiredService<IRecycleBin>().Holds(temp)).IsEqualTo(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
        using var off = new ServerFixture("--PhotoStorage:UseRecycleBin=false");
        await Assert.That(off.Services.GetRequiredService<IRecycleBin>().Holds(temp)).IsFalse();
    }
}
