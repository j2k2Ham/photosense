using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Places;
using PhotoSense.Infrastructure.Thumbnails;

namespace PhotoSense.Infrastructure;

public static class PhotoSenseCoreServices
{
    /// <summary>
    /// Everything PhotoSense runs on a computer and on a phone alike. The program it runs in adds where its
    /// settings come from and what only it has: where the photos are (<see cref="IPhotoFileSystem"/>), how
    /// pictures are decoded (<see cref="IImageAnalyzer"/>), how a file is opened elsewhere (<see cref="ISystemViewer"/>)
    /// and the folders offered to scan (<see cref="IFolderBrowser"/>).
    /// </summary>
    public static IServiceCollection AddPhotoSenseCore(this IServiceCollection s)
    {
        // Services that take an optional clock are given the real one; tests give their own.
        s.AddSingleton(TimeProvider.System);
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
        s.AddSingleton<IThumbnailStore>(sp =>
            new FileSystemThumbnailStore(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()));
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
        s.AddSingleton(sp => new PhotoRanking(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.KeepFormat));
        s.AddSingleton<IDuplicateAnalysisService, DuplicateAnalysisService>();
        s.AddSingleton<IDuplicateRemovalService, DuplicateRemovalService>();
        // Every move, copy and removal of files goes through the one journal, one at a time.
        s.AddSingleton<FileChanges>();
        s.AddSingleton<IPlaceNameResolver, GeoNamesPlaceResolver>();
        // Which picture a video belongs to is told by an identifier in both files. Organize knows it for every file
        // it has listed; for any other it is read from the file, once, and kept.
        s.AddSingleton<ICompanionFileFinder>(sp =>
        {
            var files = sp.GetRequiredService<IPhotoFileSystem>();
            return new CompanionFileFinder(files, sp.GetRequiredService<OrganizeLibrary>().LivePhotoIds(new LivePhotoIdCache(files).Read));
        });
        s.AddSingleton<IMediaDetailsReader, ExifMediaDetailsReader>();
        s.AddSingleton<IMediaDetailsStore>(sp => new LiteDbMediaDetailsStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton(sp => new OrganizeLibrary(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IMediaDetailsReader>(), sp.GetRequiredService<IImageHashingService>(),
            sp.GetRequiredService<IMediaDetailsStore>(), sp.GetRequiredService<IPhotoFileSystem>()));
        s.AddSingleton<PlaceLookup>();
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
}
