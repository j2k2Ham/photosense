namespace PhotoSense.iOS;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Say.Line($"started, version {NSBundle.MainBundle.InfoDictionary["CFBundleShortVersionString"]} ({NSBundle.MainBundle.InfoDictionary["CFBundleVersion"]})");
        PhoneApp.PutBackUnfinishedChanges();
        return true;
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
        => new("Default Configuration", connectingSceneSession.Role);
}

[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        Window ??= new UIWindow(windowScene);
        Window.RootViewController = new PageViewController();
        Window.MakeKeyAndVisible();
    }
}
