using Photos;
using WebKit;

namespace PhotoSense.iOS;

/// <summary>The page, filling the screen, with every request it makes answered inside the app.</summary>
public sealed class PageViewController : UIViewController
{
    // A scheme of the app's own: a web view hands such requests to the app, as it never would http or https.
    private static readonly NSUrl Start = new("app://photosense/");
    private WKWebView? _web;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(new PageSchemeHandler(), "app");
        SelfCheck.AddTo(config);
        _web = new WKWebView(View!.Bounds, config) { AutoresizingMask = UIViewAutoresizing.FlexibleDimensions };
        // Lets Safari on a Mac look inside the page while the phone is plugged in.
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4)) _web.Inspectable = true;
        View.AddSubview(_web);
    }

    public override async void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (_web is null || _web.Url is not null) return;
        // Asked once, before the page first shows: all the photos, some of them, or none. The page shows what was allowed.
        await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite);
        _web.LoadRequest(new NSUrlRequest(Start));
    }
}
