namespace PhotoSense.iOS;

/// <summary>The app as iOS starts it: the scene that shows the page.</summary>
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var info = NSBundle.MainBundle.InfoDictionary;
        Say.Line($"started, version {info?["CFBundleShortVersionString"]} ({info?["CFBundleVersion"]})");
        PhoneApp.PutBackUnfinishedChanges();
        return true;
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
}
