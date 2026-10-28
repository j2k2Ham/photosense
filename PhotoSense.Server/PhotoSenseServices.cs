using Microsoft.Extensions.Options;
using PhotoSense.Api.About;
using PhotoSense.Desktop.Browsing;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Desktop.Viewing;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Server;

public static class PhotoSenseServices
{
    /// <summary>PhotoSense as it runs on a computer, with its settings read from <paramref name="cfg"/>.</summary>
    public static IServiceCollection AddPhotoSenseComputer(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<PhotoStorageOptions>(cfg.GetSection("PhotoStorage"));
        s.AddSingleton<IValidateOptions<PhotoStorageOptions>, PhotoStorageOptionsValidator>();
        // Where the photos are: this computer's disks. On a phone it is the phone's photo library.
        s.AddSingleton<IPhotoFileSystem, DiskFileSystem>();
        s.AddSingleton<IImageAnalyzer, MagickImageAnalyzer>();
        s.AddSingleton<ISystemViewer, ShellSystemViewer>();
        s.AddSingleton<IFolderBrowser, FileSystemFolderBrowser>();
        s.AddSingleton<IPhotoAccess, FolderAccess>();
        s.AddSingleton(ProgramInfo.From(typeof(PhotoSenseServices).Assembly, "server"));
        s.AddHostedService<PutBackUnfinishedChanges>();
        return s.AddPhotoSenseCore();
    }
}
