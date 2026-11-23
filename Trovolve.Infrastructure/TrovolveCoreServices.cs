using System.Diagnostics.CodeAnalysis;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Trovolve.Application.Changes;
using Trovolve.Application.Organizing;
using Trovolve.Application.Removal;
using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Hashing;
using Trovolve.Infrastructure.Metadata;
using Trovolve.Infrastructure.Persistence;
using Trovolve.Infrastructure.Places;
using Trovolve.Infrastructure.Thumbnails;

namespace Trovolve.Infrastructure;

/// <summary>The services Trovolve runs on every host, for each host to add to its own.</summary>
public static class TrovolveCoreServices
{
    /// <summary>
    /// The stores, readers and resolvers Trovolve runs on a computer and on a phone alike, behind the interfaces of
    /// <c>Trovolve.Domain</c>. The program it runs in adds Trovolve's own services (<c>AddTrovolveApplication</c>),
    /// where its settings come from and what only it has: where the photos are (<see cref="IPhotoFileSystem"/>), how
    /// pictures are decoded (<see cref="IImageAnalyzer"/>), how a file is opened elsewhere (<see cref="ISystemViewer"/>)
    /// and the folders offered to scan (<see cref="IFolderBrowser"/>).
    /// </summary>
    /// <param name="s">The services to add to.</param>
    public static IServiceCollection AddTrovolveCore(this IServiceCollection s)
    {
        // The stores stamp what they keep with this clock; tests give their own.
        s.TryAddSingleton(TimeProvider.System);
        s.AddSingleton(sp => OpenDatabase(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value));
        // Every store shares the one database connection.
        s.AddSingleton<IPhotoRepository>(sp => new LiteDbPhotoRepository(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IAuditRepository>(sp => new LiteDbAuditRepository(sp.GetRequiredService<LiteDatabase>(), sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<IScanHistory>(sp => new LiteDbScanHistory(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IOrganizeBatchStore>(sp => new LiteDbOrganizeBatchStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IPairingStore>(sp => new LiteDbPairingStore(sp.GetRequiredService<LiteDatabase>(), sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<IPlanStore>(sp => new LiteDbPlanStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IBackupStore>(sp => new LiteDbBackupStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<ISwipeStore>(sp => new LiteDbSwipeStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<INoticeStore>(sp => new LiteDbNoticeStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IBackupLedger>(sp => new LiteDbBackupLedger(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<ITripStore>(sp => new LiteDbTripStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<ILocationStore>(sp => new LiteDbLocationStore(sp.GetRequiredService<LiteDatabase>()));
        s.AddSingleton<IDetailsFixStore>(sp => new LiteDbDetailsFixStore(sp.GetRequiredService<LiteDatabase>()));
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
            return new CompanionFileFinder(files, sp.GetRequiredService<OrganizeLibrary>().LivePhotoIds(new LivePhotoIdCache(files).ReadAsync));
        });
        s.AddSingleton<IMediaDetailsReader, ExifMediaDetailsReader>();
        s.AddSingleton<IMediaDetailsStore>(sp => new LiteDbMediaDetailsStore(sp.GetRequiredService<LiteDatabase>()));
        return s;
    }

    // The database file every store shares, in a folder made for it if there is none yet. No test opens a database file
    // (Jamie, 2026-11-10): the store tests use one in memory, and the end-to-end tests run the program with its own.
    [ExcludeFromCodeCoverage(Justification = "Opens the database file, which no test does; the end-to-end tests run it.")]
    private static LiteDatabase OpenDatabase(PhotoStorageOptions options)
    {
        Directory.CreateDirectory(options.ResolveDatabaseFolder());
        return new LiteDatabase(options.ResolveDatabasePath());
    }
}
