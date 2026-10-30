using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Infrastructure.Library;

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
    /// <param name="advertiser">Tells computers on the network that the phone is offering photos.</param>
    /// <param name="name">What the phone is called, as a computer shows it.</param>
    /// <param name="root">Where the library is shown; tests give a folder of their own.</param>
    /// <param name="listener">Waits for computers to connect; the phone's own network by default.</param>
    public static IServiceCollection AddPhotoSensePhone(this IServiceCollection s, IPhotoLibrary library, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, string name, string root = LibraryRoot, ITransferListener? listener = null)
    {
        var photos = new PhotoLibraryFileSystem(library, root);
        s.AddSingleton(photos);
        return s.AddPhone(photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, name, listener);
    }

    /// <summary>PhotoSense as it runs on an Android phone: as <see cref="AddPhotoSensePhone"/>, on the photos of the phone's own storage.</summary>
    public static IServiceCollection AddPhotoSenseAndroid(this IServiceCollection s, IMediaStore store, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, string name, string root = StorageRoot, ITransferListener? listener = null)
    {
        var photos = new MediaFileSystem(store, root);
        s.AddSingleton(photos);
        return s.AddPhone(photos, root, access, analyzer, dataFolder, cacheFolder, advertiser, name, listener);
    }

    private static IServiceCollection AddPhone(this IServiceCollection s, IPhotoFileSystem photos, string root, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, ITransferAdvertiser advertiser, string name, ITransferListener? listener)
    {
        s.AddSingleton(Options.Create(new PhotoStorageOptions
        {
            DatabasePath = Path.Combine(dataFolder, "photosense.db"),
            ThumbnailPath = Path.Combine(cacheFolder, "thumbnails"),
        }));
        s.AddSingleton(photos);
        s.AddSingleton(access);
        s.AddSingleton(analyzer);
        s.AddSingleton<ISystemViewer, PhoneViewer>();
        s.AddSingleton<IFolderBrowser>(new PhotoLibraryBrowser(photos, Path.TrimEndingDirectorySeparator(root)));
        s.AddSingleton(sp => new PhoneSending(listener ?? new TcpTransferListener(), advertiser, sp.GetRequiredService<IPairingStore>(), photos,
            sp.GetRequiredService<ICompanionFileFinder>(), sp.GetRequiredService<OrganizeLibrary>(), name));
        return s.AddPhotoSenseCore();
    }
}
