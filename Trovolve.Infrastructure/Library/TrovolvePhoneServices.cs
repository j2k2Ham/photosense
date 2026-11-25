using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trovolve.Domain.Configuration;
using Trovolve.Application.Organizing;
using Trovolve.Application.Plans;
using Trovolve.Application.Removal;
using Trovolve.Application.Transfer;
using Trovolve.Application.Vault;
using Trovolve.Application.Locations;
using Trovolve.Infrastructure.Locations;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Metadata;
using Trovolve.Infrastructure.Transfer;
using Trovolve.Infrastructure.Vault;

using Trovolve.Application;

namespace Trovolve.Infrastructure.Library;

/// <summary>The services Trovolve runs on a phone, for the phone apps to add to theirs.</summary>
public static class TrovolvePhoneServices
{
    /// <summary>Where an iPhone's photo library is shown, as a folder.</summary>
    public const string LibraryRoot = "/Photo Library";

    /// <summary>Where an Android phone's storage is shown, as a folder.</summary>
    public const string StorageRoot = "/Phone";

    /// <summary>
    /// Trovolve as it runs on a phone: on the photos in its library, as far as <paramref name="access"/> lets it see, decoded the phone's own way, with what it
    /// records kept in <paramref name="dataFolder"/> and its previews in <paramref name="cacheFolder"/>, which the
    /// phone may empty when it runs short of space.
    /// </summary>
    /// <param name="s">The services to add to.</param>
    /// <param name="library">The phone's photo library.</param>
    /// <param name="access">What the person has let Trovolve see.</param>
    /// <param name="analyzer">Decodes pictures the phone's own way.</param>
    /// <param name="dataFolder">Where what Trovolve records is kept.</param>
    /// <param name="cacheFolder">Where previews are kept; the phone may empty it.</param>
    /// <param name="advertiser">Tells computers on the network that the phone is offering photos.</param>
    /// <param name="appStore">The phone's own store, where Pro is bought.</param>
    /// <param name="scanner">The phone's camera, which scans a computer's pairing code.</param>
    /// <param name="computers">Finds the computers with Trovolve on the phone's Wi-Fi.</param>
    /// <param name="device">What sort of phone it is, whether it is charging, its storage and whether its photos are in iCloud too.</param>
    /// <param name="notifier">The phone's own notifications.</param>
    /// <param name="share">The phone's own share sheet, for the cards.</param>
    /// <param name="vaultKeys">Where the hidden vault's recovery key is kept: the Keychain, or the Android Keystore.</param>
    /// <param name="unlocker">The phone's own question before the hidden vault opens.</param>
    /// <param name="widgets">The phone's home screen widgets.</param>
    /// <param name="locationSecrets">Where the backup locations' keys, passwords and tokens are kept: the Keychain, or the Android Keystore.</param>
    /// <param name="signIn">The system's sheet the person signs in to a cloud service on.</param>
    /// <param name="name">What the phone is called, as a computer shows it.</param>
    /// <param name="root">Where the library is shown; tests give a folder of their own.</param>
    /// <param name="listener">Waits for computers to connect; the phone's own network by default.</param>
    /// <param name="cloudApps">Trovolve's registrations with the cloud services; none until it is registered with them.</param>
    /// <param name="network">Sends the locations' requests; the phone's own network by default.</param>
    public static IServiceCollection AddTrovolvePhone(this IServiceCollection s, IPhotoLibrary library, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore,
        ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device, INotifier notifier, IShareSheet share, IVaultKeyStore vaultKeys, IUnlocker unlocker, IWidgets widgets,
        ILocationSecrets locationSecrets, IWebSignIn signIn, string name, string root = LibraryRoot, ITransferListener? listener = null, CloudAppOptions? cloudApps = null,
        HttpMessageHandler? network = null)
    {
        var photos = new PhotoLibraryFileSystem(library, root);
        s.AddSingleton(photos);
        s.AddLocations(locationSecrets, signIn, cloudApps, network);
        return s.AddPhone(photos, photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, appStore, scanner, computers, device, notifier, share, vaultKeys, unlocker, widgets, name, listener);
    }

    /// <summary>Trovolve as it runs on an Android phone: as <see cref="AddTrovolvePhone"/>, on the photos of the phone's own storage.</summary>
    /// <param name="s">The services to add to.</param>
    /// <param name="store">The phone's media store.</param>
    /// <param name="access">What the person has let Trovolve see.</param>
    /// <param name="analyzer">Decodes pictures the phone's own way.</param>
    /// <param name="dataFolder">Where what Trovolve records is kept.</param>
    /// <param name="cacheFolder">Where previews are kept; the phone may empty it.</param>
    /// <param name="advertiser">Tells computers on the network that the phone is offering photos.</param>
    /// <param name="appStore">The phone's own store, where Pro is bought.</param>
    /// <param name="scanner">The phone's camera, which scans a computer's pairing code.</param>
    /// <param name="computers">Finds the computers with Trovolve on the phone's Wi-Fi.</param>
    /// <param name="device">What sort of phone it is, whether it is charging, its storage and whether its photos are in iCloud too.</param>
    /// <param name="notifier">The phone's own notifications.</param>
    /// <param name="share">The phone's own share sheet, for the cards.</param>
    /// <param name="vaultKeys">Where the hidden vault's recovery key is kept: the Keychain, or the Android Keystore.</param>
    /// <param name="unlocker">The phone's own question before the hidden vault opens.</param>
    /// <param name="widgets">The phone's home screen widgets.</param>
    /// <param name="locationSecrets">Where the backup locations' keys, passwords and tokens are kept: the Keychain, or the Android Keystore.</param>
    /// <param name="signIn">The system's sheet the person signs in to a cloud service on.</param>
    /// <param name="name">What the phone is called, as a computer shows it.</param>
    /// <param name="root">Where the storage is shown; tests give a folder of their own.</param>
    /// <param name="listener">Waits for computers to connect; the phone's own network by default.</param>
    /// <param name="cloudApps">Trovolve's registrations with the cloud services; none until it is registered with them.</param>
    /// <param name="network">Sends the locations' requests; the phone's own network by default.</param>
    /// <param name="vaultFolder">Where the hidden vault's files are kept; a folder inside <paramref name="dataFolder"/> by default. Android's own backup of the app stops for good once it grows past 25 MB, so the app keeps them where that backup does not go.</param>
    public static IServiceCollection AddTrovolveAndroid(this IServiceCollection s, IMediaStore store, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore,
        ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device, INotifier notifier, IShareSheet share, IVaultKeyStore vaultKeys, IUnlocker unlocker, IWidgets widgets,
        ILocationSecrets locationSecrets, IWebSignIn signIn, string name, string root = StorageRoot, ITransferListener? listener = null, CloudAppOptions? cloudApps = null,
        HttpMessageHandler? network = null, string? vaultFolder = null)
    {
        var photos = new MediaFileSystem(store, root);
        s.AddSingleton(photos);
        s.AddLocations(locationSecrets, signIn, cloudApps, network);
        return s.AddPhone(photos, photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, appStore, scanner, computers, device, notifier, share, vaultKeys, unlocker, widgets, name, listener,
            vaultFolder);
    }

    // Signing in to the cloud services and reaching each location's folder, over one client for every request.
    private static void AddLocations(this IServiceCollection s, ILocationSecrets secrets, IWebSignIn signIn, CloudAppOptions? apps, HttpMessageHandler? network)
    {
        // A large video may take a while to send: a request is stopped by its token, not by a short time limit.
        var http = new HttpClient(network ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromHours(1) };
        s.AddSingleton(secrets);
        s.AddSingleton(signIn);
        s.AddSingleton(sp => new CloudAccounts(apps ?? new CloudAppOptions(), http, sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<ICloudAccounts>(sp => sp.GetRequiredService<CloudAccounts>());
        s.AddSingleton<IRemoteFolders>(sp => new RemoteFolders(sp.GetRequiredService<CloudAccounts>(), http));
    }

    private static IServiceCollection AddPhone<TPhotos>(this IServiceCollection s, TPhotos photos, IVideoCompressor compressor, string root, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore, ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device,
        INotifier notifier, IShareSheet share, IVaultKeyStore vaultKeys, IUnlocker unlocker, IWidgets widgets, string name, ITransferListener? listener, string? vaultFolder = null)
        where TPhotos : class, IPhotoFileSystem, IPhotoChangesTogether, IFavoritePhotos, ISharingCopies, IPhotoDetails, IPhotoImport
    {
        // The hidden vault's own files, beside what Trovolve records, where the phone never empties them.
        var vault = new FolderVaultFiles(vaultFolder ?? Path.Combine(dataFolder, "Hidden"));
        s.AddSingleton(Options.Create(new PhotoStorageOptions
        {
            DatabasePath = Path.Combine(dataFolder, "trovolve.db"),
            ThumbnailPath = Path.Combine(cacheFolder, "thumbnails"),
        }));
        s.AddSingleton<IPhotoFileSystem>(photos);
        s.AddSingleton(access);
        s.AddSingleton(analyzer);
        s.AddSingleton(appStore);
        s.AddSingleton<ISystemViewer, PhoneViewer>();
        s.AddSingleton<IFolderBrowser>(new PhotoLibraryBrowser(photos, Path.TrimEndingDirectorySeparator(root)));
        s.AddSingleton(sp => new PhoneSending(listener ?? new TcpTransferListener(), advertiser, sp.GetRequiredService<IPairingStore>(), photos,
            sp.GetRequiredService<ICompanionFileFinder>(), sp.GetRequiredService<OrganizeLibrary>(), sp.GetRequiredService<Subscription>(), name, sp.GetRequiredService<TimeProvider>()));
        // Pairing with a computer: by its QR code, or by its code typed, found on the Wi-Fi.
        s.AddSingleton(sp => new PhonePairing(scanner, computers, new TcpComputerConnector(), sp.GetRequiredService<IPairingStore>(), sp.GetRequiredService<IBackupLedger>(), name, sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<TermsAcceptance>()));
        // Backing up to that computer, and what is then safe to delete from the phone.
        s.AddSingleton(device);
        s.AddSingleton(sp => new PhoneBackup(sp.GetRequiredService<IBackupLedger>(), computers, sp.GetRequiredService<IPairingStore>(), sp.GetRequiredService<OrganizeLibrary>(), photos,
            sp.GetRequiredService<Subscription>(), sp.GetRequiredService<DailyRemovals>(), device, photos, name, Path.TrimEndingDirectorySeparator(root), sp.GetRequiredService<TimeProvider>(), vault,
            sp.GetRequiredService<ILocationStore>(), sp.GetRequiredService<TermsAcceptance>()));
        // The other places it backs up to: cloud services and those Connect to server adds.
        s.AddSingleton(sp => new BackupLocations(sp.GetRequiredService<ILocationStore>(), sp.GetRequiredService<ILocationSecrets>(), sp.GetRequiredService<IRemoteFolders>(),
            sp.GetRequiredService<ICloudAccounts>(), sp.GetRequiredService<IWebSignIn>(), sp.GetRequiredService<PhoneBackup>(), sp.GetRequiredService<IBackupLedger>(),
            sp.GetRequiredService<OrganizeLibrary>(), photos, photos, sp.GetRequiredService<Subscription>(), sp.GetRequiredService<DailyRemovals>(), device, Path.TrimEndingDirectorySeparator(root),
            sp.GetRequiredService<TimeProvider>()));
        // Swipe to clean: a set a day, what is marked deleted together at the end.
        s.AddSingleton(sp => new SwipeToClean(sp.GetRequiredService<ISwipeStore>(), sp.GetRequiredService<OrganizeLibrary>(), photos, sp.GetRequiredService<PhoneBackup>(),
            sp.GetRequiredService<DailyRemovals>(), Path.TrimEndingDirectorySeparator(root), sp.GetRequiredService<TimeProvider>()));
        // Compressing large videos, and the notifications: the low-storage alert and the trial's reminder.
        s.AddSingleton(notifier);
        s.AddSingleton(sp => new VideoCompression(sp.GetRequiredService<CleanUpCategories>(), sp.GetRequiredService<IPhotoRepository>(), compressor, photos, sp.GetRequiredService<Subscription>(),
            sp.GetRequiredService<DailyRemovals>()));
        s.AddSingleton(sp => new PhoneNotices(sp.GetRequiredService<INoticeStore>(), notifier, device, sp.GetRequiredService<CleanUpCategories>(), sp.GetRequiredService<Subscription>(),
            sp.GetRequiredService<TimeProvider>()));
        // Search, favorites, and copies to share without their place: Library and its viewer.
        s.AddSingleton<IFavoritePhotos>(photos);
        s.AddSingleton<ISharingCopies>(photos);
        s.AddSingleton(sp => new PhotoSearch(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<Subscription>()));
        // Trips, away from home; Year in review counts them, and its cards and the result screens' go to the share sheet.
        s.AddSingleton(sp => new Trips(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<ITripStore>()));
        s.AddSingleton(share);
        s.AddSingleton(sp => new YearInReview(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<IPlanStore>(),
            sp.GetRequiredService<ISwipeStore>(), sp.GetRequiredService<Trips>(), sp.GetRequiredService<TimeProvider>()));
        // Fix dates and places: the phone's own dates and places, changed after it asks, each fix kept by the phone's id.
        s.AddSingleton<IPhotoDetails>(photos);
        s.AddSingleton(sp => new PhotoFixes(sp.GetRequiredService<IPhotoRepository>(), photos, sp.GetRequiredService<IDetailsFixStore>(), sp.GetRequiredService<IMediaDetailsStore>(),
            sp.GetRequiredService<OrganizeLibrary>(), sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<Subscription>(), sp.GetRequiredService<TimeProvider>()));
        // The home screen widgets, handed what to show.
        s.AddSingleton(sp => new PhoneWidgets(widgets, device, sp.GetRequiredService<CleanUpCategories>(), sp.GetRequiredService<PhoneBackup>(), sp.GetRequiredService<SwipeToClean>()));
        // The hidden vault: sealed inside Trovolve, behind the phone's own question, carried to the computer sealed.
        s.AddSingleton(sp => new HiddenVault(vaultKeys, unlocker, vault, photos, photos, photos, sp.GetRequiredService<OrganizeLibrary>(), analyzer, sp.GetRequiredService<IPhotoRepository>(),
            sp.GetRequiredService<IThumbnailStore>(), sp.GetRequiredService<Subscription>(), sp.GetRequiredService<DailyRemovals>(), sp.GetRequiredService<PhoneBackup>(),
            Path.TrimEndingDirectorySeparator(root), sp.GetRequiredService<TimeProvider>()));
        s.AddTrovolveApplication().AddTrovolveCore();
        // Registered after the core's own, so that a fix holds whatever a file says, Photos on an iPhone leaving the file as it was.
        s.AddSingleton<IPhotoMetadataExtractor>(sp => new FixedPhotoMetadata(new BasicExifMetadataExtractor(), photos, sp.GetRequiredService<IDetailsFixStore>()));
        s.AddSingleton<IMediaDetailsReader>(sp => new FixedMediaDetails(new ExifMediaDetailsReader(photos), photos, sp.GetRequiredService<IDetailsFixStore>()));
        return s;
    }
}
