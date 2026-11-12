using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

namespace PhotoSense.Application;

/// <summary>What PhotoSense decides, registered once for every program: scanning, matching, Organize, changes and removal.</summary>
public static class PhotoSenseApplicationServices
{
    /// <summary>
    /// Registers PhotoSense's own services. They work through the interfaces of <c>PhotoSense.Domain</c>, which each
    /// program registers for itself: where the photos are, the stores, and the readers of pictures.
    /// </summary>
    /// <param name="s">The program's registrations.</param>
    public static IServiceCollection AddPhotoSenseApplication(this IServiceCollection s)
    {
        // Every service that stamps a time takes it from here; tests give their own clock.
        s.TryAddSingleton(TimeProvider.System);
        s.AddSingleton(sp => new PhotoRanking(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.KeepFormat));
        s.AddSingleton<IDuplicateAnalysisService, DuplicateAnalysisService>();
        s.AddSingleton<IDuplicateRemovalService, DuplicateRemovalService>();
        // Every move, copy and removal of files goes through the one journal, one at a time.
        s.AddSingleton<FileChanges>();
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
            sp.GetRequiredService<IPhotoFileSystem>(), sp.GetRequiredService<IScanLogSink>(), history: sp.GetRequiredService<IScanHistory>(),
            time: sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<ScanRunner>();
        return s;
    }
}
