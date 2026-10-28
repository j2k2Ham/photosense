using Photos;
using WebKit;

namespace PhotoSense.iOS;

/// <summary>The page, filling the screen, with every request it makes answered inside the app.</summary>
public sealed class PageViewController : UIViewController
{
    // A scheme of the app's own: a web view hands such requests to the app, as it never would http or https.
    private static readonly NSUrl Start = new("app://photosense/");
    private WKWebView? _web;

    /// <summary>The page on screen, which the system's own pickers are shown over.</summary>
    public static PageViewController? Shown { get; private set; }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Shown = this;
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(new PageSchemeHandler(), "app");
        SelfCheck.AddTo(config);
        _web = new WKWebView(View!.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions, NavigationDelegate = new Loading(), UIDelegate = new NewWindows(),
        };
        // Lets Safari on a Mac look inside the page while the phone is plugged in.
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4)) _web.Inspectable = true;
        View.AddSubview(_web);
    }

    public override async void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (_web is null || _web.Url is not null) return;
        // Asked once, before the page first shows: all the photos, some of them, or none. The page shows what was allowed.
        var access = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite);
        Say.Line($"photo access: {access} (add only: {PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.AddOnly)})");
        // On GitHub's simulator nobody can answer the system's question, so the check runs with what was granted.
        if (access == PHAuthorizationStatus.NotDetermined && !SelfCheck.Running)
            Say.Line($"photo access chosen: {await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite)}");
        _web.LoadRequest(new NSUrlRequest(Start));
        Say.Line($"loading {Start}");
    }

    private static void OpenOutside(NSUrl url) => UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);

    private sealed class Loading : WKNavigationDelegate
    {
        // The page's own addresses stay in the app; a link anywhere else (a web site, an email address) opens outside it.
        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var url = navigationAction.Request.Url;
            if (url.Scheme is "app" or "about") { decisionHandler(WKNavigationActionPolicy.Allow); return; }
            decisionHandler(WKNavigationActionPolicy.Cancel);
            OpenOutside(url);
        }

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation) => Say.Line($"loaded {webView.Url}");

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error) => Say.Line($"the page failed: {error.LocalizedDescription}");

        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error) => Say.Line($"the page could not load: {error.LocalizedDescription}");

        public override void ContentProcessDidTerminate(WKWebView webView) => Say.Line("the web view's process stopped");
    }

    // A link meant for a new window (target="_blank") would otherwise do nothing in a web view.
    private sealed class NewWindows : WKUIDelegate
    {
        public override WKWebView? CreateWebView(WKWebView webView, WKWebViewConfiguration configuration, WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
        {
            OpenOutside(navigationAction.Request.Url);
            return null;
        }
    }
}
