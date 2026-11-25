using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Runtime;
using AndroidX.Work;
using Trovolve.Domain.Services;

namespace Trovolve.AndroidApp;

/// <summary>
/// Trovolve's notifications on Android: in a channel of their own, after the person allows them (asked on Android 13
/// and later); the low-storage alert with Review and Not now; one to come later, the trial's reminder, through WorkManager.
/// </summary>
public sealed class AndroidNotifier(Context context) : INotifier
{
    /// <summary>The intent's extra that says what a notification tapped opens.</summary>
    public const string OpensExtra = "trovolve-opens";

    private const string Channel = "trovolve";
    private static string? _opened;

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    /// <summary>Notes what a notification tapped asked to open, from the intent that opened the app.</summary>
    public static void Opened(Intent? intent)
    {
        if (intent?.GetStringExtra(OpensExtra) is { } opens) _opened = opens;
    }

    /// <inheritdoc />
    public Task<bool> AllowedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Allowed);
    }

    private bool Allowed => Manager.AreNotificationsEnabled()
        && (!OperatingSystem.IsAndroidVersionAtLeast(33) || context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted);

    /// <inheritdoc />
    public async Task<bool> AskAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Before Android 13 an app may send them unless the person turned them off in Settings, where only they can turn them on.
        if (Allowed || !OperatingSystem.IsAndroidVersionAtLeast(33)) return Allowed;
        return await MainActivity.AskPermission(Manifest.Permission.PostNotifications) && Allowed;
    }

    /// <inheritdoc />
    public void Show(Notice notice)
    {
        if (Manager.GetNotificationChannel(Channel) is null)
            Manager.CreateNotificationChannel(new NotificationChannel(Channel, "Trovolve", NotificationImportance.Default) { Description = "Low storage, and the end of a free trial." });
        var number = Number(notice.Id);
        var open = Open(notice, number);
        // Android draws a notification's small icon from its shape alone, so the full-colour app icon would be a white square.
        var icon = Icon.CreateWithResource(context, Resource.Drawable.notice_icon);
        var builder = new Notification.Builder(context, Channel).SetSmallIcon(icon).SetContentTitle(notice.Title).SetContentText(notice.Body).SetAutoCancel(true).SetContentIntent(open);
        if (notice.Id == Notices.LowStorage)
        {
            var notNow = new Intent(context, typeof(NoticeReceiver)).PutExtra(NoticeReceiver.NumberExtra, number);
            builder.AddAction(new Notification.Action.Builder(icon, "Review", open).Build())
                .AddAction(new Notification.Action.Builder(icon, "Not now", PendingIntent.GetBroadcast(context, number, notNow, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)).Build());
        }
        Manager.Notify(number, builder.Build());
    }

    /// <inheritdoc />
    public void Schedule(Notice notice, DateTimeOffset at)
    {
        var data = new Data.Builder().PutString("id", notice.Id)!.PutString("title", notice.Title)!.PutString("body", notice.Body)!.PutString("opens", notice.Opens)!.Build();
        var delay = (long)Math.Max(0, (at - DateTimeOffset.UtcNow).TotalMilliseconds);
        var request = new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(NoticeWorker)))
            .SetInitialDelay(delay, Java.Util.Concurrent.TimeUnit.Milliseconds!)!.SetInputData(data)!.Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork(notice.Id, ExistingWorkPolicy.Replace!, (OneTimeWorkRequest)request);
    }

    /// <inheritdoc />
    public void Cancel(string id)
    {
        WorkManager.GetInstance(context).CancelUniqueWork(id);
        Manager.Cancel(Number(id));
    }

    /// <inheritdoc />
    public string? TakeOpened() => Interlocked.Exchange(ref _opened, null);

    // The app, opened where the notification says.
    private PendingIntent Open(Notice notice, int number)
    {
        var intent = new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop)!.PutExtra(OpensExtra, notice.Opens)!;
        return PendingIntent.GetActivity(context, number, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    // Android numbers its notifications: each of Trovolve's has one of its own.
    private static int Number(string id) => id switch { Notices.LowStorage => 1, Notices.TrialReminder => 2, _ => 3 };
}

/// <summary>Shows a notification that was to come later, such as the trial's reminder, when WorkManager runs it.</summary>
[Register("com.j2k2ham.trovolve.NoticeWorker")]
public sealed class NoticeWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    /// <inheritdoc />
    public override Result DoWork()
    {
        var data = InputData!;
        new AndroidNotifier(ApplicationContext!).Show(new Notice(data.GetString("id") ?? Notices.TrialReminder, data.GetString("title") ?? "", data.GetString("body") ?? "", data.GetString("opens")));
        return Result.InvokeSuccess()!;
    }
}

/// <summary>Not now, on the low-storage alert: puts the notification away.</summary>
[BroadcastReceiver(Name = "com.j2k2ham.trovolve.NoticeReceiver", Exported = false)]
public sealed class NoticeReceiver : BroadcastReceiver
{
    /// <summary>The intent's extra holding the notification's number.</summary>
    public const string NumberExtra = "trovolve-notice";

    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context?.GetSystemService(Context.NotificationService) is NotificationManager manager) manager.Cancel(intent?.GetIntExtra(NumberExtra, 0) ?? 0);
    }
}
