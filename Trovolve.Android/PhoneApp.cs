using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Http;
using Trovolve.Application.Changes;
using Trovolve.Application.Plans;
using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Domain.Entities;
using Trovolve.Infrastructure.Library;
using Trovolve.Infrastructure.Transfer;

namespace Trovolve.AndroidApp;

/// <summary>Trovolve's services as the app runs them, made once.</summary>
public static class PhoneApp
{
    // A Debug build, as made to try the app, offers plans for testing; a Release build, as sent to the store, never does.
#if DEBUG
    private static readonly DevMode Dev = new(true);
#else
    private static readonly DevMode Dev = DevMode.Off;
#endif

    private static ServiceProvider? _built;
    private static Task? _ready;

    /// <summary>The app's services, once it has started.</summary>
    public static IServiceProvider Services => _built ?? throw new InvalidOperationException("The app has not started.");

    /// <summary>The service's addresses, which answer the page's requests.</summary>
    public static ApiRouter Router => Services.GetRequiredService<TrovolveRoutes>().Router;

    /// <summary>Done once a change that a stop of the app cut short has been put back; nothing is answered before.</summary>
    public static Task Ready => _ready ?? Task.CompletedTask;

    /// <summary>Where the app keeps what it records, which the phone backs up with it.</summary>
    public static string DataFolder(Context context) => context.FilesDir!.AbsolutePath;

    /// <summary>Builds the app's services, once, and starts putting back what a stop cut short.</summary>
    public static void Start(Context context)
    {
        if (_built is not null) return;
        _built = new ServiceCollection()
            .AddTrovolveAndroid(new AndroidMediaStore(context), new AndroidAccess(), new AndroidImageAnalyzer(), DataFolder(context), context.CacheDir!.AbsolutePath,
                new NsdAdvertiser(context), new PlayBillingStore(TimeProvider.System),
                new GoogleCodeScanner(), new MdnsComputerFinder(), new AndroidDevice(context), new AndroidNotifier(context), new AndroidShareSheet(context),
                new KeystoreVaultKeys(context), new DeviceUnlocker(), new AndroidWidgets(context), new KeystoreLocationSecrets(context), new WebSignIn(),
                $"{Build.Manufacturer} {Build.Model}".Trim(),
                vaultFolder: Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "Hidden"))
            .AddTrovolveApi()
            .AddSingleton(ProgramInfo.From(typeof(PhoneApp).Assembly, "android"))
            .AddSingleton(Dev)
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
        // Nothing stops it part way: it reads what Trovolve knows and draws two widgets.
        await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(CancellationToken.None);
    }
}
