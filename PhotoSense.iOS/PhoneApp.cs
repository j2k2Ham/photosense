using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.iOS;

/// <summary>PhotoSense's services as the app runs them, made once.</summary>
public static class PhoneApp
{
    private static readonly Lazy<ServiceProvider> Built = new(() => new ServiceCollection()
        .AddPhotoSensePhone(new PhotoKitLibrary(DataFolder), new PhotoKitAccess(), new ImageIOAnalyzer(), DataFolder, Folder(NSSearchPathDirectory.CachesDirectory), new BonjourAdvertiser(), UIDevice.CurrentDevice.Name)
        .AddPhotoSenseApi()
        .AddSingleton(ProgramInfo.From(typeof(PhoneApp).Assembly, "iphone"))
        .BuildServiceProvider());

    private static Task? _ready;

    /// <summary>The app's services, once it has started.</summary>
    public static IServiceProvider Services => Built.Value;

    /// <summary>The service's addresses, which answer the page's requests.</summary>
    public static ApiRouter Router => Services.GetRequiredService<PhotoSenseRoutes>().Router;

    /// <summary>Where the app keeps what it records, which the phone backs up with it.</summary>
    public static string DataFolder => Folder(NSSearchPathDirectory.DocumentDirectory);

    /// <summary>Done once a change that a stop of the app cut short has been put back; nothing is answered before.</summary>
    public static Task Ready => _ready ?? Task.CompletedTask;

    /// <summary>Puts back, off the main thread, the changes a stop of the app cut short.</summary>
    public static void PutBackUnfinishedChanges() => _ready = Task.Run(async () =>
    {
        var log = Services.GetRequiredService<IScanLogSink>();
        // Putting back runs once, at start, until it is done: nothing stops it part way.
        foreach (var sentence in await Services.GetRequiredService<FileChanges>().RecoverAsync(CancellationToken.None)) log.Log("audit", "Warning", sentence);
    });

    private static string Folder(NSSearchPathDirectory which) => NSFileManager.DefaultManager.GetUrls(which, NSSearchPathDomain.User)[0].Path!;
}
