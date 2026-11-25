using Trovolve.Application.Locations;
using Android.Content;
using Android.Runtime;
using AndroidX.Concurrent.Futures;
using AndroidX.Work;
using Google.Common.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Application.Plans;
using Trovolve.Application.Transfer;

namespace Trovolve.AndroidApp;

/// <summary>
/// Automatic backup with the app closed: WorkManager wakes the app about once an hour while the phone is on Wi-Fi, and
/// Trovolve backs up if one is due (automatic backup on and part of the plan, the phone charging if it should be, the
/// computer paired, something waiting). The work is answered by a future the backup completes, so nothing blocks on it.
/// </summary>
[Register("com.j2k2ham.trovolve.BackupWorker")]
public sealed class BackupWorker : ListenableWorker
{
    /// <summary>The unique name the work is kept under, so that it is scheduled once however often the app starts.</summary>
    public const string Name = "trovolve-backup";

    private readonly CancellationTokenSource _stop = new();

    /// <summary>Made by WorkManager, by this class's Java name, each time the work runs.</summary>
    public BackupWorker(Context context, WorkerParameters parameters) : base(context, parameters) { }

    /// <summary>Schedules the work, about once an hour on Wi-Fi, keeping it as it is when it is scheduled already.</summary>
    public static void Schedule(Context context)
    {
        var onWifi = new Constraints.Builder().SetRequiredNetworkType(NetworkType.Unmetered!).Build();
        var request = new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BackupWorker)), 1, Java.Util.Concurrent.TimeUnit.Hours!)
            .SetConstraints(onWifi).Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(Name, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    /// <inheritdoc />
    public override IListenableFuture StartWork() => CallbackToFutureAdapter.GetFuture(new Run(this))!;

    /// <summary>Android stops the work, as when the Wi-Fi goes: the backup going on pauses, and carries on next time.</summary>
    public override void OnStopped()
    {
        _stop.Cancel();
        base.OnStopped();
    }

    // Backs up if one is due, and says the work is done once the backup has ended, however it ended.
    private sealed class Run(BackupWorker worker) : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        public Java.Lang.Object? AttachCompleter(CallbackToFutureAdapter.Completer? completer)
        {
            _ = BackUpAsync(completer!);
            return "Trovolve automatic backup";
        }

        private async Task BackUpAsync(CallbackToFutureAdapter.Completer completer)
        {
            var stop = worker._stop.Token;
            try
            {
                PhoneApp.Start(worker.ApplicationContext!);
                await PhoneApp.Ready;
                var backup = PhoneApp.Services.GetRequiredService<PhoneBackup>();
                using var pausing = stop.Register(backup.Pause);
                if (await backup.AutomaticAsync(stop)) await backup.Running;
                // Then the other locations: cloud storage and those Connect to server added.
                var locations = PhoneApp.Services.GetRequiredService<BackupLocations>();
                using var pausingLocations = stop.Register(locations.Pause);
                if (await locations.AutomaticAsync(stop)) await locations.Running;
                // Woken anyway: a low-storage alert goes if one is due.
                await PhoneApp.Services.GetRequiredService<PhoneNotices>().CheckStorageAsync(stop);
                // And the home screen widgets are told how things stand.
                await PhoneApp.Services.GetRequiredService<PhoneWidgets>().RefreshAsync(stop);
            }
            // Stopped by Android, or the backup could not start: it is tried again at the next hour, and the app shows why.
            catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
            {
                Android.Util.Log.Info("Trovolve", $"Automatic backup did not run: {ex.Message}");
            }
            finally
            {
                completer.Set(Result.InvokeSuccess());
            }
        }
    }
}
