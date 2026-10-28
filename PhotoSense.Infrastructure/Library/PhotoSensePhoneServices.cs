using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Library;

public static class PhotoSensePhoneServices
{
    /// <summary>Where a phone's photo library is shown, as a folder.</summary>
    public const string LibraryRoot = "/Photo Library";

    /// <summary>
    /// PhotoSense as it runs on a phone: on the photos in its library, as far as <paramref name="access"/> lets it see, decoded the phone's own way, with what it
    /// records kept in <paramref name="dataFolder"/> and its previews in <paramref name="cacheFolder"/>, which the
    /// phone may empty when it runs short of space.
    /// </summary>
    /// <param name="root">Where the library is shown; tests give a folder of their own.</param>
    public static IServiceCollection AddPhotoSensePhone(this IServiceCollection s, IPhotoLibrary library, IPhotoAccess access, IImageAnalyzer analyzer,
        string dataFolder, string cacheFolder, string root = LibraryRoot)
    {
        s.AddSingleton(Options.Create(new PhotoStorageOptions
        {
            DatabasePath = Path.Combine(dataFolder, "photosense.db"),
            ThumbnailPath = Path.Combine(cacheFolder, "thumbnails"),
        }));
        var photos = new PhotoLibraryFileSystem(library, root);
        s.AddSingleton(photos);
        s.AddSingleton<IPhotoFileSystem>(photos);
        s.AddSingleton(access);
        s.AddSingleton(analyzer);
        s.AddSingleton<ISystemViewer, PhoneViewer>();
        s.AddSingleton<IFolderBrowser, PhotoLibraryBrowser>();
        return s.AddPhotoSenseCore();
    }
}
