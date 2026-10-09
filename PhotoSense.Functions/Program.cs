using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Browsing;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Imaging;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Places;
using PhotoSense.Infrastructure.Thumbnails;
using PhotoSense.Infrastructure.Viewing;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Infrastructure.Events;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Application.Photos.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Infrastructure.Scanning;
using LiteDB;
using Microsoft.Extensions.Options; // added for IValidateOptions
using PhotoSense.Functions.Scanning;

namespace PhotoSense.Functions;

public static class DependencyInjection
{
    public static IServiceCollection AddPhotoSenseCore(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<PhotoStorageOptions>(cfg.GetSection("PhotoStorage"));
        s.Configure<MessagingOptions>(cfg.GetSection("Messaging"));
        s.AddSingleton<IValidateOptions<PhotoStorageOptions>, PhotoStorageOptionsValidator>();
        s.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value;
            Directory.CreateDirectory(opts.ResolveDatabaseFolder());
            return new LiteDatabase(opts.ResolveDatabasePath());
        });
        // Every store shares the one database connection.
        s.AddSingleton<IPhotoRepository>(sp => new LiteDbPhotoRepository(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IAuditRepository>(sp => new LiteDbAuditRepository(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IScanHistory>(sp => new LiteDbScanHistory(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IOrganizeBatchStore>(sp => new LiteDbOrganizeBatchStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IImageHashingService, Sha256ImageHashingService>();
        s.AddSingleton<IImageAnalyzer, MagickImageAnalyzer>();
        s.AddSingleton<IThumbnailStore>(sp =>
            new FileSystemThumbnailStore(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()));
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
        s.AddSingleton(sp => new PhotoRanking(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value.KeepFormat));
        s.AddSingleton<IDuplicateAnalysisService, DuplicateAnalysisService>();
        s.AddSingleton<IDuplicateRemovalService, DuplicateRemovalService>();
        s.AddSingleton<IPlaceNameResolver, GeoNamesPlaceResolver>();
        s.AddSingleton<PhotoDtoMapper>();
        s.AddSingleton<ScanGroupingFacade>();
        s.AddSingleton<IScanRequestPublisher, ScanRequestPublisher>();
        s.AddSingleton<IOutboxStore, LiteDbOutboxStore>();
        s.AddSingleton<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        // Which picture a video belongs to is told by an identifier in both files. Organize knows it for every file
        // it has listed; for any other it is read from the file, once, and kept.
        s.AddSingleton<ICompanionFileFinder>(sp => new CompanionFileFinder(sp.GetRequiredService<OrganizeLibrary>().LivePhotoIds(new LivePhotoIdCache().Read)));
        s.AddSingleton<ISystemViewer, ShellSystemViewer>();
        s.AddSingleton<IFolderBrowser, FileSystemFolderBrowser>();
        s.AddSingleton<IPhotoDeletionService, FileSystemPhotoDeletionService>();
        s.AddSingleton<IPhotoSearchService, PhotoSearchService>();
        s.AddSingleton<IMediaDetailsReader, ExifMediaDetailsReader>();
        s.AddSingleton<IMediaDetailsStore>(sp => new LiteDbMediaDetailsStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton(sp => new OrganizeLibrary(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IMediaDetailsReader>(), sp.GetRequiredService<IImageHashingService>(),
            sp.GetRequiredService<IMediaDetailsStore>()));
        s.AddSingleton<PlaceLookup>();
        s.AddSingleton<Api.OrganizeListingMapper>();
        s.AddSingleton<OrganizePlanner>();
        s.AddSingleton<OrganizeMover>();
        s.AddSingleton(sp => new RemovedFiles(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IOrganizeBatchStore>(), sp.GetRequiredService<IAuditRepository>()));
        s.AddSingleton<IScanProgressStore, InMemoryScanProgressStore>();
        s.AddSingleton<IScanLogSink, InMemoryScanLogSink>();
        s.AddSingleton<IScanExecutionService>(sp => new ScanExecutionService(
            sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IImageHashingService>(), sp.GetRequiredService<IImageAnalyzer>(),
            sp.GetRequiredService<IPhotoMetadataExtractor>(), sp.GetRequiredService<IThumbnailStore>(), sp.GetRequiredService<IScanProgressStore>(),
            sp.GetRequiredService<IScanLogSink>(), history: sp.GetRequiredService<IScanHistory>()));
        return s;
    }
}

// Starts the Functions worker process; there is nothing here to exercise without the Functions host.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.AddEnvironmentVariables();
            })
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices((ctx, s) =>
            {
                s.AddPhotoSenseCore(ctx.Configuration);
            })
            .Build();
        await host.RunAsync();
    }
}
