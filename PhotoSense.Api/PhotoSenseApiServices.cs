using PhotoSense.Application.Organizing;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Transfer;
using PhotoSense.Application.Vault;
using PhotoSense.Application.Locations;
using PhotoSense.Application.Plans;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Application.Removal;
using PhotoSense.Api.About;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Scanning;

namespace PhotoSense.Api;

/// <summary>The endpoints, for every host to add to its services.</summary>
public static class PhotoSenseApiServices
{
    /// <summary>The endpoints, the addresses that reach them, and what runs scans in the background.</summary>
    /// <param name="s">The services to add to.</param>
    public static IServiceCollection AddPhotoSenseApi(this IServiceCollection s)
    {
        s.AddSingleton<PhotoDtoMapper>();
        s.AddSingleton<ScanGroupingFacade>();
        s.AddSingleton<OrganizeListingMapper>();
        s.AddSingleton<ScanRunner>();
        s.AddSingleton(ProductInfo.Bundled);
        s.AddSingleton<HealthEndpoints>();
        s.AddSingleton<AboutEndpoints>();
        s.AddSingleton<FoldersEndpoints>();
        s.AddSingleton<LibraryEndpoints>();
        s.AddSingleton<PhotosEndpoints>();
        s.AddSingleton<DuplicateGroupsEndpoints>();
        s.AddSingleton<ScanStatusEndpoints>();
        s.AddSingleton<ScanStartEndpoints>();
        s.AddSingleton<ScanResetEndpoints>();
        s.AddSingleton<ScanProgressEndpoints>();
        s.AddSingleton<ScanFoundEndpoints>();
        s.AddSingleton<ScanLogsEndpoints>();
        s.AddSingleton<OrganizeEndpoints>();
        s.AddSingleton<RemovedEndpoints>();
        // A phone tells the computer it was paired with when the person accepts the terms; a computer has no phone's backup.
        s.AddSingleton(sp => new PlanEndpoints(sp.GetRequiredService<Subscription>(), sp.GetRequiredService<DailyRemovals>(), sp.GetRequiredService<TermsAcceptance>(), sp.GetService<PhoneBackup>()));
        // A computer pairs by showing a code, a phone by scanning or typing it: each program has the one half.
        s.AddSingleton(sp => new PairingEndpoints(sp.GetService<ComputerPairing>(), sp.GetService<PhonePairing>()));
        // A phone backs up and a computer keeps the backups: each program has the one half.
        s.AddSingleton(sp => new BackupEndpoints(sp.GetService<PhoneBackup>(), sp.GetService<ComputerBackup>(), sp.GetRequiredService<OrganizeLibrary>()));
        // Only a phone has Swipe to clean.
        s.AddSingleton(sp => new SwipeEndpoints(sp.GetService<SwipeToClean>()));
        s.AddSingleton<CategoryEndpoints>();
        // Only a phone compresses videos and sends notifications.
        s.AddSingleton(sp => new CompressionEndpoints(sp.GetService<VideoCompression>(), sp.GetRequiredService<PhotoDtoMapper>()));
        s.AddSingleton(sp => new NoticeEndpoints(sp.GetService<PhoneNotices>()));
        s.AddSingleton(sp => new YearEndpoints(sp.GetService<YearInReview>(), sp.GetService<IShareSheet>()));
        s.AddSingleton(sp => new ViewerEndpoints(sp.GetService<PhotoSearch>(), sp.GetService<IFavoritePhotos>(), sp.GetService<ISharingCopies>(), sp.GetService<IShareSheet>(),
            sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<PhotoDtoMapper>()));
        // Only a phone fixes dates and places.
        s.AddSingleton(sp => new FixEndpoints(sp.GetService<PhotoFixes>(), sp.GetService<PhoneBackup>(), sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<PhotoDtoMapper>()));
        // Only a phone has Trips.
        s.AddSingleton(sp => new VaultEndpoints(sp.GetService<HiddenVault>()));
        // Only a phone backs up to locations.
        s.AddSingleton(sp => new LocationEndpoints(sp.GetService<BackupLocations>()));
        s.AddSingleton(sp => new TripEndpoints(sp.GetService<Trips>(), sp.GetService<PhoneBackup>(), sp.GetRequiredService<PhotoDtoMapper>()));
        // A phone sends and a computer receives: each program has the one half.
        s.AddSingleton(sp => new TransferEndpoints(sp.GetService<PhoneSending>(), sp.GetService<PhoneReceiving>()));
        // Made here: the endpoints it is given are this project's own.
        s.AddSingleton(sp => new PhotoSenseRoutes(
            sp.GetRequiredService<HealthEndpoints>(), sp.GetRequiredService<AboutEndpoints>(), sp.GetRequiredService<FoldersEndpoints>(),
            sp.GetRequiredService<PhotosEndpoints>(), sp.GetRequiredService<DuplicateGroupsEndpoints>(), sp.GetRequiredService<ScanStatusEndpoints>(),
            sp.GetRequiredService<ScanStartEndpoints>(), sp.GetRequiredService<ScanResetEndpoints>(), sp.GetRequiredService<ScanProgressEndpoints>(),
            sp.GetRequiredService<ScanLogsEndpoints>(), sp.GetRequiredService<OrganizeEndpoints>(), sp.GetRequiredService<LibraryEndpoints>(),
            sp.GetRequiredService<RemovedEndpoints>(), sp.GetRequiredService<TransferEndpoints>(), sp.GetRequiredService<PlanEndpoints>(),
            sp.GetRequiredService<ScanFoundEndpoints>(), sp.GetRequiredService<PairingEndpoints>(), sp.GetRequiredService<BackupEndpoints>(), sp.GetRequiredService<SwipeEndpoints>(), sp.GetRequiredService<CategoryEndpoints>(),
            sp.GetRequiredService<CompressionEndpoints>(), sp.GetRequiredService<NoticeEndpoints>(), sp.GetRequiredService<YearEndpoints>(), sp.GetRequiredService<ViewerEndpoints>(),
            sp.GetRequiredService<TripEndpoints>(), sp.GetRequiredService<FixEndpoints>(), sp.GetRequiredService<VaultEndpoints>(), sp.GetRequiredService<LocationEndpoints>()));
        return s;
    }
}
