using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.AndroidApp;

/// <summary>PhotoSense's services as the app runs them, made once.</summary>
public static class PhoneApp
{
    private static ServiceProvider? _built;
    private static Task? _ready;

    /// <summary>The app's services, once it has started.</summary>
    public static IServiceProvider Services => _built ?? throw new InvalidOperationException("The app has not started.");

    /// <summary>The service's addresses, which answer the page's requests.</summary>
    public static ApiRouter Router => Services.GetRequiredService<PhotoSenseRoutes>().Router;

    /// <summary>Done once a change that a stop of the app cut short has been put back; nothing is answered before.</summary>
    public static Task Ready => _ready ?? Task.CompletedTask;

    /// <summary>Where the app keeps what it records, which the phone backs up with it.</summary>
    public static string DataFolder(Context context) => context.FilesDir!.AbsolutePath;

    /// <summary>Builds the app's services, once, and starts putting back what a stop cut short.</summary>
    public static void Start(Context context)
    {
        if (_built is not null) return;
        _built = new ServiceCollection()
            .AddPhotoSenseAndroid(new AndroidMediaStore(context), new AndroidAccess(), new AndroidImageAnalyzer(), DataFolder(context), context.CacheDir!.AbsolutePath,
                new NsdAdvertiser(context), $"{Build.Manufacturer} {Build.Model}".Trim())
            .AddPhotoSenseApi()
            .AddSingleton(ProgramInfo.From(typeof(PhoneApp).Assembly, "android"))
            .BuildServiceProvider();
        _ready = Task.Run(async () =>
        {
            var log = Services.GetRequiredService<IScanLogSink>();
            // Putting back runs once, at start, until it is done: nothing stops it part way.
            foreach (var sentence in await Services.GetRequiredService<FileChanges>().RecoverAsync(CancellationToken.None)) log.Log("audit", "Warning", sentence);
        });
    }
}
