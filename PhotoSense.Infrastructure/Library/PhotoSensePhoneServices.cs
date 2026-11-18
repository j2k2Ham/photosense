using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Transfer;

using PhotoSense.Application;

namespace PhotoSense.Infrastructure.Library;

/// <summary>The services PhotoSense runs on a phone, for the phone apps to add to theirs.</summary>
public static class PhotoSensePhoneServices
{
    /// <summary>Where an iPhone's photo library is shown, as a folder.</summary>
    public const string LibraryRoot = "/Photo Library";

    /// <summary>Where an Android phone's storage is shown, as a folder.</summary>
    public const string StorageRoot = "/Phone";

    /// <summary>
    /// PhotoSense as it runs on a phone: on the photos in its library, as far as <paramref name="access"/> lets it see, decoded the phone's own way, with what it
    /// records kept in <paramref name="dataFolder"/> and its previews in <paramref name="cacheFolder"/>, which the
    /// phone may empty when it runs short of space.
    /// </summary>
    /// <param name="s">The services to add to.</param>
    /// <param name="library">The phone's photo library.</param>
    /// <param name="access">What the person has let PhotoSense see.</param>
    /// <param name="analyzer">Decodes pictures the phone's own way.</param>
    /// <param name="dataFolder">Where what PhotoSense records is kept.</param>
    /// <param name="cacheFolder">Where previews are kept; the phone may empty it.</param>
    /// <param name="advertiser">Tells computers on the network that the phone is offering photos.</param>
    /// <param name="appStore">The phone's own store, where Pro is bought.</param>
    /// <param name="scanner">The phone's camera, which scans a computer's pairing code.</param>
    /// <param name="computers">Finds the computers with PhotoSense on the phone's Wi-Fi.</param>
    /// <param name="device">What sort of phone it is, whether it is charging, its storage and whether its photos are in iCloud too.</param>
    /// <param name="notifier">The phone's own notifications.</param>
    /// <param name="share">The phone's own share sheet, for the cards.</param>
    /// <param name="name">What the phone is called, as a computer shows it.</param>
    /// <param name="root">Where the library is shown; tests give a folder of their own.</param>
    /// <param name="listener">Waits for computers to connect; the phone's own network by default.</param>
    public static IServiceCollection AddPhotoSensePhone(this IServiceCollection s, IPhotoLibrary library, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore,
        ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device, INotifier notifier, IShareSheet share, string name, string root = LibraryRoot, ITransferListener? listener = null)
    {
        var photos = new PhotoLibraryFileSystem(library, root);
        s.AddSingleton(photos);
        return s.AddPhone(photos, photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, appStore, scanner, computers, device, notifier, share, name, listener);
    }

    /// <summary>PhotoSense as it runs on an Android phone: as <see cref="AddPhotoSensePhone"/>, on the photos of the phone's own storage.</summary>
    /// <param name="s">The services to add to.</param>
    /// <param name="store">The phone's media store.</param>
    /// <param name="access">What the person has let PhotoSense see.</param>
    /// <param name="analyzer">Decodes pictures the phone's own way.</param>
    /// <param name="dataFolder">Where what PhotoSense records is kept.</param>
    /// <param name="cacheFolder">Where previews are kept; the phone may empty it.</param>
    /// <param name="advertiser">Tells computers on the network that the phone is offering photos.</param>
    /// <param name="appStore">The phone's own store, where Pro is bought.</param>
    /// <param name="scanner">The phone's camera, which scans a computer's pairing code.</param>
    /// <param name="computers">Finds the computers with PhotoSense on the phone's Wi-Fi.</param>
    /// <param name="device">What sort of phone it is, whether it is charging, its storage and whether its photos are in iCloud too.</param>
    /// <param name="notifier">The phone's own notifications.</param>
    /// <param name="share">The phone's own share sheet, for the cards.</param>
    /// <param name="name">What the phone is called, as a computer shows it.</param>
    /// <param name="root">Where the storage is shown; tests give a folder of their own.</param>
    /// <param name="listener">Waits for computers to connect; the phone's own network by default.</param>
    public static IServiceCollection AddPhotoSenseAndroid(this IServiceCollection s, IMediaStore store, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore,
        ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device, INotifier notifier, IShareSheet share, string name, string root = StorageRoot, ITransferListener? listener = null)
    {
        var photos = new MediaFileSystem(store, root);
        s.AddSingleton(photos);
        return s.AddPhone(photos, photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, appStore, scanner, computers, device, notifier, share, name, listener);
    }

    private static IServiceCollection AddPhone<TPhotos>(this IServiceCollection s, TPhotos photos, IVideoCompressor compressor, string root, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, IAppStore appStore, ICodeScanner scanner, IComputerFinder computers, IDeviceStatus device,
        INotifier notifier, IShareSheet share, string name, ITransferListener? listener)
        where TPhotos : class, IPhotoFileSystem, IPhotoChangesTogether
    {
        s.AddSingleton(Options.Create(new PhotoStorageOptions
        {
            DatabasePath = Path.Combine(dataFolder, "photosense.db"),
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
        s.AddSingleton(sp => new PhonePairing(scanner, computers, new TcpComputerConnector(), sp.GetRequiredService<IPairingStore>(), sp.GetRequiredService<IBackupLedger>(), name, sp.GetRequiredService<TimeProvider>()));
        // Backing up to that computer, and what is then safe to delete from the phone.
        s.AddSingleton(device);
        s.AddSingleton(sp => new PhoneBackup(sp.GetRequiredService<IBackupLedger>(), computers, sp.GetRequiredService<IPairingStore>(), sp.GetRequiredService<OrganizeLibrary>(), photos,
            sp.GetRequiredService<Subscription>(), sp.GetRequiredService<DailyRemovals>(), device, photos as IFavoritePhotos, name, Path.TrimEndingDirectorySeparator(root), sp.GetRequiredService<TimeProvider>()));
        // Swipe to clean: a set a day, what is marked deleted together at the end.
        s.AddSingleton(sp => new SwipeToClean(sp.GetRequiredService<ISwipeStore>(), sp.GetRequiredService<OrganizeLibrary>(), photos, sp.GetRequiredService<PhoneBackup>(),
            sp.GetRequiredService<DailyRemovals>(), Path.TrimEndingDirectorySeparator(root), sp.GetRequiredService<TimeProvider>()));
        // Compressing large videos, and the notifications: the low-storage alert and the trial's reminder.
        s.AddSingleton(notifier);
        s.AddSingleton(sp => new VideoCompression(sp.GetRequiredService<CleanUpCategories>(), sp.GetRequiredService<IPhotoRepository>(), compressor, photos, sp.GetRequiredService<Subscription>(),
            sp.GetRequiredService<DailyRemovals>()));
        s.AddSingleton(sp => new PhoneNotices(sp.GetRequiredService<INoticeStore>(), notifier, device, sp.GetRequiredService<CleanUpCategories>(), sp.GetRequiredService<Subscription>(),
            sp.GetRequiredService<TimeProvider>()));
        // Year in review, and the share sheet its cards and the result screens' go to.
        s.AddSingleton(share);
        s.AddSingleton(sp => new YearInReview(sp.GetRequiredService<IPhotoRepository>(), sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<IPlanStore>(),
            sp.GetRequiredService<ISwipeStore>(), sp.GetRequiredService<TimeProvider>()));
        return s.AddPhotoSenseApplication().AddPhotoSenseCore();
    }
}
