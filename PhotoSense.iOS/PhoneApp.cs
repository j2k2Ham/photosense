using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.Http;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.iOS;

/// <summary>PhotoSense's services as the app runs them, made once.</summary>
public static class PhoneApp
{
    private static readonly Lazy<ServiceProvider> Built = new(() => new ServiceCollection()
        .AddPhotoSensePhone(new PhotoKitLibrary(), new ImageIOAnalyzer(), DataFolder, Folder(NSSearchPathDirectory.CachesDirectory))
        .AddPhotoSenseApi()
        .BuildServiceProvider());

    private static Task? _ready;

    public static IServiceProvider Services => Built.Value;

    public static ApiRouter Router => Services.GetRequiredService<PhotoSenseRoutes>().Router;

    /// <summary>Where the app keeps what it records, which the phone backs up with it.</summary>
    public static string DataFolder => Folder(NSSearchPathDirectory.DocumentDirectory);

    /// <summary>Done once a change that a stop of the app cut short has been put back; nothing is answered before.</summary>
    public static Task Ready => _ready ?? Task.CompletedTask;

    public static void PutBackUnfinishedChanges() => _ready = Task.Run(async () =>
    {
        var log = Services.GetRequiredService<IScanLogSink>();
        foreach (var sentence in await Services.GetRequiredService<FileChanges>().RecoverAsync()) log.Log("audit", "Warning", sentence);
    });

    private static string Folder(NSSearchPathDirectory which) => NSFileManager.DefaultManager.GetUrls(which, NSSearchPathDomain.User)[0].Path!;
}
