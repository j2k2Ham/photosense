using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Places;
using PhotoSense.Infrastructure.Thumbnails;

namespace PhotoSense.Infrastructure;

/// <summary>The services PhotoSense runs on every host, for each host to add to its own.</summary>
public static class PhotoSenseCoreServices
{
    /// <summary>
    /// The stores, readers and resolvers PhotoSense runs on a computer and on a phone alike, behind the interfaces of
    /// <c>PhotoSense.Domain</c>. The program it runs in adds PhotoSense's own services (<c>AddPhotoSenseApplication</c>),
    /// where its settings come from and what only it has: where the photos are (<see cref="IPhotoFileSystem"/>), how
    /// pictures are decoded (<see cref="IImageAnalyzer"/>), how a file is opened elsewhere (<see cref="ISystemViewer"/>)
    /// and the folders offered to scan (<see cref="IFolderBrowser"/>).
    /// </summary>
    /// <param name="s">The services to add to.</param>
    public static IServiceCollection AddPhotoSenseCore(this IServiceCollection s)
    {
        // The stores stamp what they keep with this clock; tests give their own.
        s.TryAddSingleton(TimeProvider.System);
        s.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value;
            Directory.CreateDirectory(opts.ResolveDatabaseFolder());
            return new LiteDatabase(opts.ResolveDatabasePath());
        });
        // Every store shares the one database connection.
        s.AddSingleton<IPhotoRepository>(sp => new LiteDbPhotoRepository(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IAuditRepository>(sp => new LiteDbAuditRepository(sp.GetRequiredService<LiteDatabase>(), sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<IScanHistory>(sp => new LiteDbScanHistory(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IOrganizeBatchStore>(sp => new LiteDbOrganizeBatchStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IPairingStore>(sp => new LiteDbPairingStore(sp.GetRequiredService<LiteDatabase>(), sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<IImageHashingService, Sha256ImageHashingService>();
        s.AddSingleton<IThumbnailStore>(sp =>
            new FileSystemThumbnailStore(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()));
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
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
        return s;
    }
}
