using CoreFoundation;
using Microsoft.Extensions.DependencyInjection;
using Network;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Http;
using Trovolve.Application.Changes;
using Trovolve.Application.Locations;
using Trovolve.Application.Plans;
using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Application.Transfer;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Infrastructure.Library;

namespace Trovolve.iOS;

/// <summary>Trovolve's services as the app runs them, made once.</summary>
public static class PhoneApp
{
    // A Debug build, as made to try the app, offers plans for testing; a Release build, as sent to the store, never does.
#if DEBUG
    private static readonly DevMode Dev = new(true);
#else
    private static readonly DevMode Dev = DevMode.Off;
#endif

    private static readonly Lazy<ServiceProvider> Built = new(() => new ServiceCollection()
        .AddTrovolvePhone(new PhotoKitLibrary(DataFolder), new PhotoKitAccess(), new ImageIOAnalyzer(), DataFolder, Folder(NSSearchPathDirectory.CachesDirectory), new BonjourAdvertiser(), new StoreKitStore(),
            new CameraCodeScanner(), new BonjourComputerFinder(), new IosDevice(), IosNotifier.Shared, new IosShareSheet(), new KeychainVaultKeys(), new DeviceUnlocker(),
            new IosWidgets(), new KeychainLocationSecrets(), new WebSignIn(), UIDevice.CurrentDevice.Name)
        .AddTrovolveApi()
        .AddSingleton(ProgramInfo.From(typeof(PhoneApp).Assembly, "iphone"))
        .AddSingleton(Dev)
        .BuildServiceProvider());

    private static Task? _ready;

    /// <summary>The app's services, once it has started.</summary>
    public static IServiceProvider Services => Built.Value;

    /// <summary>The service's addresses, which answer the page's requests.</summary>
    public static ApiRouter Router => Services.GetRequiredService<TrovolveRoutes>().Router;

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
        // The trial's reminder follows the plan.
        await Services.GetRequiredService<PhoneNotices>().RemindAsync(CancellationToken.None);
        // The home screen widgets show how things stand now.
        await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(CancellationToken.None);
    });

    /// <summary>Sends a low-storage alert if one is due, once the app is ready.</summary>
    public static async Task CheckStorageAsync()
    {
        await Ready;
        // Nothing stops a check part way: it reads the storage and sends one notification at most.
        await Services.GetRequiredService<PhoneNotices>().CheckStorageAsync(CancellationToken.None);
    }

    /// <summary>Hands the home screen widgets how things stand, once the app is ready.</summary>
    public static async Task RefreshWidgetsAsync()
    {
        await Ready;
        // Nothing stops it part way: it reads what Trovolve knows and writes one small file.
        await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(CancellationToken.None);
    }

    /// <summary>How the phone backs up by itself, once the app is ready: whether iOS is to be asked to wake it to back up, and whether only while charging.</summary>
    public static async Task<BackupSettings> BackupSettingsAsync()
    {
        await Ready;
        // Answered at once from what the phone keeps.
        return await Services.GetRequiredService<IBackupLedger>().SettingsAsync(CancellationToken.None);
    }

    /// <summary>
    /// What iOS's background processing task runs, as Android's hourly work does: a backup to the computer when one is
    /// due, then to the other locations, unless the phone is on mobile data or a hotspot; then the storage check and the
    /// widgets. Stopped when iOS's time is up, a backup going on pauses, and carries on the next time.
    /// </summary>
    public static async Task BackUpInBackgroundAsync(CancellationToken stop)
    {
        try
        {
            await Ready;
            if (await OnWifiAsync(stop))
            {
                var backup = Services.GetRequiredService<PhoneBackup>();
                using (stop.Register(backup.Pause)) if (await backup.AutomaticAsync(stop)) await backup.Running;
                // Then the other locations: cloud storage and those Connect to server added.
                var locations = Services.GetRequiredService<BackupLocations>();
                using (stop.Register(locations.Pause)) if (await locations.AutomaticAsync(stop)) await locations.Running;
            }
            else Say.Line("automatic backup waits for Wi-Fi");
            await Services.GetRequiredService<PhoneNotices>().CheckStorageAsync(stop);
            await Services.GetRequiredService<PhoneWidgets>().RefreshAsync(stop);
        }
        // Stopped by iOS, no network to be had, or the backup could not start: it is tried again next time, and the app shows why.
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or InvalidOperationException)
        {
            Say.Line($"automatic backup did not run: {ex.Message}");
        }
    }

    // Whether the phone is on a network not charged by use: backups with the app closed go over Wi-Fi only, as on Android.
    private static async Task<bool> OnWifiAsync(CancellationToken ct)
    {
        var monitor = new NWPathMonitor();
        var now = new TaskCompletionSource<NWPath>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotHandler = path => now.TrySetResult(path);
        monitor.SetQueue(DispatchQueue.DefaultGlobalQueue);
        monitor.Start();
        try
        {
            // The monitor says how the network is as soon as it starts.
            var path = await now.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return path.Status == NWPathStatus.Satisfied && !path.IsExpensive;
        }
        finally
        {
            monitor.Cancel();
        }
    }

    private static string Folder(NSSearchPathDirectory which) => NSFileManager.DefaultManager.GetUrls(which, NSSearchPathDomain.User)[0].Path!;
}
