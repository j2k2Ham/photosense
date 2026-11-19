using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;

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
                new NsdAdvertiser(context), new PlayBillingStore(TimeProvider.System),
                new GoogleCodeScanner(), new MdnsComputerFinder(), new AndroidDevice(context), new AndroidNotifier(context), new AndroidShareSheet(context),
                new KeystoreVaultKeys(context), new DeviceUnlocker(), new AndroidWidgets(context), $"{Build.Manufacturer} {Build.Model}".Trim(),
                vaultFolder: Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "Hidden"))
            .AddPhotoSenseApi()
            .AddSingleton(ProgramInfo.From(typeof(PhoneApp).Assembly, "android"))
            .BuildServiceProvider();
        _ready = Task.Run(async () =>
        {
            var log = Services.GetRequiredService<IScanLogSink>();
            // Putting back runs once, at start, until it is done: nothing stops it part way.
            foreach (var sentence in await Services.GetRequiredService<FileChanges>().RecoverAsync(CancellationToken.None)) log.Log("audit", "Warning", sentence);
            // The trial's reminder follows the plan, and a low-storage alert goes if one is due.
            var notices = Services.GetRequiredService<PhoneNotices>();
            await notices.RemindAsync(CancellationToken.None);
            await notices.CheckStorageAsync(CancellationToken.None);
            // The home screen widgets show how things stand now.
            await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(CancellationToken.None);
        });
    }

    /// <summary>Hands the home screen widgets how things stand, once the app is ready; as the app goes off the screen.</summary>
    public static async Task RefreshWidgetsAsync()
    {
        await Ready;
        // Nothing stops it part way: it reads what PhotoSense knows and draws two widgets.
        await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(CancellationToken.None);
    }
}
