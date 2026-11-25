using BackgroundTasks;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Application.Plans;
using Trovolve.Application.Vault;
using UserNotifications;

namespace Trovolve.iOS;

/// <summary>The app as iOS starts it: the scene that shows the page, notifications tapped, the check for low storage, and automatic backup with the app closed.</summary>
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    /// <summary>The background refresh in which iOS, when it chooses, lets Trovolve check for low storage.</summary>
    public const string StorageCheck = "com.j2k2ham.trovolve.storage-check";

    /// <summary>The background processing task in which iOS, when it chooses, lets Trovolve back up with the app closed.</summary>
    public const string Backup = "com.j2k2ham.trovolve.backup";

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var info = NSBundle.MainBundle.InfoDictionary;
        Say.Line($"started, version {info?["CFBundleShortVersionString"]} ({info?["CFBundleVersion"]})");
        // Told of a notification tapped from the start, as the tap may be what opened the app.
        UNUserNotificationCenter.Current.Delegate = IosNotifier.Shared;
        BGTaskScheduler.Shared.Register(StorageCheck, null, Refresh);
        BGTaskScheduler.Shared.Register(Backup, null, BackUp);
        PhoneApp.PutBackUnfinishedChanges();
        _ = AskForBackupAsync();
        return true;
    }

    /// <summary>Asks iOS to wake the app in half a day or more, to check the storage; iOS decides when, if at all.</summary>
    public static void AskForRefresh()
    {
        var request = new BGAppRefreshTaskRequest(StorageCheck) { EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(TimeSpan.FromHours(12).TotalSeconds) };
        if (!BGTaskScheduler.Shared.Submit(request, out var error)) Say.Line($"background refresh not scheduled: {error?.LocalizedDescription}");
    }

    // iOS woke the app: it checks the storage, tells the widgets how things stand, asks to be woken again, and says it has finished.
    private static void Refresh(BGTask task)
    {
        AskForRefresh();
        var checking = Task.WhenAll(PhoneApp.CheckStorageAsync(), PhoneApp.RefreshWidgetsAsync());
        task.ExpirationHandler = () => task.SetTaskCompleted(false);
        checking.ContinueWith(done => task.SetTaskCompleted(done.IsCompletedSuccessfully), TaskScheduler.Default);
    }

    /// <summary>
    /// Asks iOS to wake the app to back up, an hour on at the earliest, when automatic backup is on: with a network, and
    /// on power if the person asked for backups only while charging. iOS decides when, if at all, usually while the phone
    /// charges at night. With automatic backup off, a request waiting is withdrawn.
    /// </summary>
    public static async Task AskForBackupAsync()
    {
        var settings = await PhoneApp.BackupSettingsAsync();
        if (!settings.Automatic)
        {
            BGTaskScheduler.Shared.Cancel(Backup);
            return;
        }
        var request = new BGProcessingTaskRequest(Backup)
        {
            RequiresNetworkConnectivity = true,
            RequiresExternalPower = settings.OnlyWhileCharging,
            EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(TimeSpan.FromHours(1).TotalSeconds),
        };
        if (!BGTaskScheduler.Shared.Submit(request, out var error)) Say.Line($"background backup not scheduled: {error?.LocalizedDescription}");
    }

    // iOS woke the app to back up: when its time is up the backup pauses; either way the task says it has finished, and
    // iOS is asked to wake the app again.
    private static void BackUp(BGTask task)
    {
        var stop = new CancellationTokenSource();
        task.ExpirationHandler = stop.Cancel;
        PhoneApp.BackUpInBackgroundAsync(stop.Token).ContinueWith(done =>
        {
            task.SetTaskCompleted(done.IsCompletedSuccessfully && !stop.IsCancellationRequested);
            stop.Dispose();
            _ = AskForBackupAsync();
        }, TaskScheduler.Default);
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
        => new("Default Configuration", connectingSceneSession.Role);
}

/// <summary>The scene the page is shown in.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    /// <summary>The window the page is shown in.</summary>
    [Export("window")]
    public UIWindow? Window { get; set; }

    /// <summary>Shows the page, once the scene is there to show it in.</summary>
    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        Window ??= new UIWindow(windowScene);
        Window.RootViewController = new PageViewController();
        Window.MakeKeyAndVisible();
        // Started from a widget or the app icon's menu: the page asks what to open once it is ready.
        IosNotifier.Open(connectionOptions.ShortcutItem?.Type ?? Opens(connectionOptions.UrlContexts?.AnyObject?.Url));
    }

    /// <summary>The app icon's menu tapped while Trovolve was running.</summary>
    [Export("windowScene:performActionForShortcutItem:completionHandler:")]
    public void PerformAction(UIWindowScene windowScene, UIApplicationShortcutItem shortcutItem, Action<bool> completionHandler)
    {
        IosNotifier.Open(shortcutItem.Type);
        completionHandler(true);
    }

    /// <summary>A widget tapped while Trovolve was running: trovolve://opens/backup.</summary>
    [Export("scene:openURLContexts:")]
    public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        foreach (var context in urlContexts) IosNotifier.Open(Opens(context.Url));
    }

    // What a widget's link opens: the last part of trovolve://opens/backup.
    private static string? Opens(NSUrl? url) => url?.Scheme == "trovolve" && url.Host == "opens" ? url.LastPathComponent : null;

    /// <summary>Back on screen: a low-storage alert goes if one is due.</summary>
    [Export("sceneDidBecomeActive:")]
    public void DidBecomeActive(UIScene scene) => _ = PhoneApp.CheckStorageAsync();

    /// <summary>Gone to the background: the hidden vault locks, the home screen widgets are told how things stand, and iOS is asked to wake the app later, to check the storage and to back up if automatic backup is on.</summary>
    [Export("sceneDidEnterBackground:")]
    public void DidEnterBackground(UIScene scene)
    {
        PhoneApp.Services.GetRequiredService<HiddenVault>().Lock();
        _ = PhoneApp.RefreshWidgetsAsync();
        AppDelegate.AskForRefresh();
        _ = AppDelegate.AskForBackupAsync();
    }
}
