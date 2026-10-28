using LiteDB;
using Microsoft.Extensions.Options;
using PhotoSense.Api;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Browsing;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Files;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Imaging;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Places;
using PhotoSense.Infrastructure.Thumbnails;
using PhotoSense.Infrastructure.Viewing;

namespace PhotoSense.Server;

public static class PhotoSenseServices
{
    public static IServiceCollection AddPhotoSenseCore(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<PhotoStorageOptions>(cfg.GetSection("PhotoStorage"));
        // Services that take an optional clock are given the real one; tests give their own.
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<IValidateOptions<PhotoStorageOptions>, PhotoStorageOptionsValidator>();
        // Where the photos are: this computer's disks. On a phone it is the phone's photo library.
        s.AddSingleton<IPhotoFileSystem, DiskFileSystem>();
        s.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value;
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
            new FileSystemThumbnailStore(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()));
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
        s.AddSingleton(sp => new PhotoRanking(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.KeepFormat));
        s.AddSingleton<IDuplicateAnalysisService, DuplicateAnalysisService>();
        s.AddSingleton<IDuplicateRemovalService, DuplicateRemovalService>();
        // Every move, copy and removal of files goes through the one journal, one at a time.
        s.AddSingleton<FileChanges>();
        s.AddHostedService<PutBackUnfinishedChanges>();
        s.AddSingleton<IPlaceNameResolver, GeoNamesPlaceResolver>();
        s.AddSingleton<PhotoDtoMapper>();
        s.AddSingleton<ScanGroupingFacade>();
        // Which picture a video belongs to is told by an identifier in both files. Organize knows it for every file
        // it has listed; for any other it is read from the file, once, and kept.
        s.AddSingleton<ICompanionFileFinder>(sp =>
        {
            var files = sp.GetRequiredService<IPhotoFileSystem>();
            return new CompanionFileFinder(files, sp.GetRequiredService<OrganizeLibrary>().LivePhotoIds(new LivePhotoIdCache(files).Read));
        });
        s.AddSingleton<ISystemViewer, ShellSystemViewer>();
        s.AddSingleton<IFolderBrowser, FileSystemFolderBrowser>();
        s.AddSingleton<IMediaDetailsReader, ExifMediaDetailsReader>();
        s.AddSingleton<IMediaDetailsStore>(sp => new LiteDbMediaDetailsStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton(sp => new OrganizeLibrary(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IMediaDetailsReader>(), sp.GetRequiredService<IImageHashingService>(),
            sp.GetRequiredService<IMediaDetailsStore>(), sp.GetRequiredService<IPhotoFileSystem>()));
        s.AddSingleton<PlaceLookup>();
        s.AddSingleton<OrganizeListingMapper>();
        s.AddSingleton<OrganizePlanner>();
        s.AddSingleton<OrganizeMover>();
        s.AddSingleton<RemovedFiles>();
        s.AddSingleton<IScanProgressStore, InMemoryScanProgressStore>();
        s.AddSingleton<IScanLogSink, InMemoryScanLogSink>();
        s.AddSingleton<IScanExecutionService>(sp => new ScanExecutionService(
            sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IImageHashingService>(), sp.GetRequiredService<IImageAnalyzer>(),
            sp.GetRequiredService<IPhotoMetadataExtractor>(), sp.GetRequiredService<IThumbnailStore>(), sp.GetRequiredService<IScanProgressStore>(),
            sp.GetRequiredService<IPhotoFileSystem>(), sp.GetRequiredService<IScanLogSink>(), history: sp.GetRequiredService<IScanHistory>()));
        return s;
    }

    /// <summary>The endpoints, the addresses that reach them, and what runs scans in the background.</summary>
    public static IServiceCollection AddPhotoSenseApi(this IServiceCollection s)
    {
        s.AddSingleton<ScanRunner>();
        s.AddSingleton<HealthEndpoints>();
        s.AddSingleton<FoldersEndpoints>();
        s.AddSingleton<PhotosEndpoints>();
        s.AddSingleton<DuplicateGroupsEndpoints>();
        s.AddSingleton<ScanStatusEndpoints>();
        s.AddSingleton<ScanStartEndpoints>();
        s.AddSingleton<ScanResetEndpoints>();
        s.AddSingleton<ScanProgressEndpoints>();
        s.AddSingleton<ScanLogsEndpoints>();
        s.AddSingleton<OrganizeEndpoints>();
        s.AddSingleton<RemovedEndpoints>();
        s.AddSingleton<PhotoSenseRoutes>();
        return s;
    }
}
