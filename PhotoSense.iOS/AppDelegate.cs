using BackgroundTasks;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Plans;
using UserNotifications;

namespace PhotoSense.iOS;

/// <summary>The app as iOS starts it: the scene that shows the page, notifications tapped, and the check for low storage.</summary>
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    /// <summary>The background refresh in which iOS, when it chooses, lets PhotoSense check for low storage.</summary>
    public const string StorageCheck = "com.j2k2ham.photosense.storage-check";

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var info = NSBundle.MainBundle.InfoDictionary;
        Say.Line($"started, version {info?["CFBundleShortVersionString"]} ({info?["CFBundleVersion"]})");
        // Told of a notification tapped from the start, as the tap may be what opened the app.
        UNUserNotificationCenter.Current.Delegate = IosNotifier.Shared;
        BGTaskScheduler.Shared.Register(StorageCheck, null, Refresh);
        PhoneApp.PutBackUnfinishedChanges();
        return true;
    }

    /// <summary>Asks iOS to wake the app in half a day or more, to check the storage; iOS decides when, if at all.</summary>
    public static void AskForRefresh()
    {
        var request = new BGAppRefreshTaskRequest(StorageCheck) { EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(TimeSpan.FromHours(12).TotalSeconds) };
        if (!BGTaskScheduler.Shared.Submit(request, out var error)) Say.Line($"background refresh not scheduled: {error?.LocalizedDescription}");
    }

    // iOS woke the app: it checks the storage, asks to be woken again, and says it has finished.
    private static void Refresh(BGTask task)
    {
        AskForRefresh();
        var checking = PhoneApp.CheckStorageAsync();
        task.ExpirationHandler = () => task.SetTaskCompleted(false);
        checking.ContinueWith(done => task.SetTaskCompleted(done.IsCompletedSuccessfully), TaskScheduler.Default);
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
    }

    /// <summary>Back on screen: a low-storage alert goes if one is due.</summary>
    [Export("sceneDidBecomeActive:")]
    public void DidBecomeActive(UIScene scene) => _ = PhoneApp.CheckStorageAsync();

    /// <summary>Gone to the background: iOS is asked to wake the app later, to check the storage.</summary>
    [Export("sceneDidEnterBackground:")]
    public void DidEnterBackground(UIScene scene) => AppDelegate.AskForRefresh();
}
