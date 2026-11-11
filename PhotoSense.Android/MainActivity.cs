using System.Collections.Concurrent;
using System.Globalization;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Window;
using Android.Widget;

namespace PhotoSense.AndroidApp;

/// <summary>The one screen: the page, the whole of it, under the phone's own bars, which the page keeps clear of.</summary>
[Activity(Name = "com.j2k2ham.photosense.MainActivity", Label = "PhotoSense", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : Activity
{
    private static int _nextQuestion = 100;
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> Questions = new();
    private WebView? _page;
    // The heights of the status bar and the gesture or button bar, in the page's pixels, as the page is told them.
    private (float Top, float Bottom) _insets;

    /// <summary>The screen while it is there, for what Android shows over it.</summary>
    public static MainActivity? Shown { get; private set; }

    /// <summary>What PhotoSense asks to see: the photos and videos, and where they were taken.</summary>
    public static string[] PhotoPermissions
    {
        get
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
                return [Manifest.Permission.ReadMediaImages, Manifest.Permission.ReadMediaVideo, Manifest.Permission.ReadMediaVisualUserSelected, Manifest.Permission.AccessMediaLocation];
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) return [Manifest.Permission.ReadMediaImages, Manifest.Permission.ReadMediaVideo, Manifest.Permission.AccessMediaLocation];
            return [Manifest.Permission.ReadExternalStorage, Manifest.Permission.AccessMediaLocation];
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Shown = this;
        PhoneApp.Start(ApplicationContext!);
        SelfCheck.Prepare(this, Intent);

        var page = _page = new WebView(this);
        page.Settings.JavaScriptEnabled = true;
        page.Settings.DomStorageEnabled = true;
        page.Settings.MediaPlaybackRequiresUserGesture = true;
        page.SetWebViewClient(new PageClient(ApplicationContext!));
        page.AddJavascriptInterface(new BodyBridge(), BodyBridge.Name);
        page.AddJavascriptInterface(new SystemBars(), SystemBars.Name);
        // The page fills the screen, under the status bar and the bar of buttons, painting what shows through them
        // itself; it is told how tall they are, as the web view does not say.
#pragma warning disable CA1422 // Before Android 15 the page is not drawn under the bars, and they have colours of their own that would hide it.
        Window!.SetDecorFitsSystemWindows(false);
        Window.SetStatusBarColor(Color.Transparent);
        Window.SetNavigationBarColor(Color.Transparent);
#pragma warning restore CA1422
        Window.NavigationBarContrastEnforced = false;
        var screen = new FrameLayout(this);
        screen.AddView(page, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        screen.SetOnApplyWindowInsetsListener(new UnderBars(this));
        SetContentView(screen);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault, new BackCallback(Back));

        // The page asks for the photos itself, once it has said what PhotoSense does with them (library/ask).
        page.LoadUrl(PageClient.Origin);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (Questions.TryRemove(requestCode, out var asked)) asked.TrySetResult(grantResults.Any(g => g == Permission.Granted));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (Questions.TryRemove(requestCode, out var asked)) asked.TrySetResult(resultCode == Result.Ok);
    }

    // Back goes back in the page's history, which closes the screen, sheet or view it opened last. With nothing left
    // there it puts away a window the page has open, as Esc does on a computer; with none open, it leaves the app.
    private void Back()
    {
        if (_page is null) return;
        if (_page.CanGoBack()) { _page.GoBack(); return; }
        _page.EvaluateJavascript(
            "(() => { const open = document.querySelector('[role=dialog], [role=alertdialog], [role=menu]'); if (open) window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); return !!open; })()",
            new Answered(put => { if (!put) RunOnUiThread(Finish); }));
    }

    /// <summary>Tells the page how tall the phone's bars are, as it draws under them; again whenever the page has loaded.</summary>
    public void TellInsets() => _page?.EvaluateJavascript(string.Create(CultureInfo.InvariantCulture,
        $"document.documentElement.style.setProperty('--app-inset-top','{_insets.Top}px');document.documentElement.style.setProperty('--app-inset-bottom','{_insets.Bottom}px')"), null);

    /// <summary>Draws the bars' icons dark on a light page and light on a dark one.</summary>
    public void ShowTheme(bool light)
    {
        const int both = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
        Window?.InsetsController?.SetSystemBarsAppearance(light ? both : 0, both);
    }

#pragma warning disable CA1422 // Before Android 13, the only way to hear the back button.
    public override void OnBackPressed() => Back();
#pragma warning restore CA1422

    private sealed class Answered(Action<bool> then) : Java.Lang.Object, IValueCallback
    {
        /// <summary>Takes what the page answered.</summary>
        public void OnReceiveValue(Java.Lang.Object? value) => then(value?.ToString() == "true");
    }

    private sealed class BackCallback(Action back) : Java.Lang.Object, IOnBackInvokedCallback
    {
        /// <summary>Goes back in the page, or leaves the app when there is nowhere to go back to.</summary>
        public void OnBackInvoked() => back();
    }

    protected override void OnDestroy()
    {
        if (Shown == this)
        {
            Shown = null;
            // The screen is going for good, so no answer will come to a question still up: it counts as no, and
            // nothing it asked about is changed. One only being redrawn, as for a new language, still gets the answer.
            if (!IsChangingConfigurations)
                foreach (var id in Questions.Keys)
                    if (Questions.TryRemove(id, out var asked)) asked.TrySetResult(false);
        }
        base.OnDestroy();
    }

    /// <summary>Shows a question of Android's own, as before photos are moved or put in the bin, and waits for the answer.</summary>
    /// <returns>Whether the person said yes.</returns>
    public static bool Ask(PendingIntent question)
    {
        var screen = Shown ?? throw new IOException("PhotoSense has to be open on the screen to ask before it changes photos.");
        var (id, answer) = Question();
        screen.RunOnUiThread(() => screen.StartIntentSenderForResult(question.IntentSender, id, null, 0, 0, 0));
        return answer.Task.GetAwaiter().GetResult();
    }

    /// <summary>Asks again for what PhotoSense may see, which on Android 14 lets the person choose more photos.</summary>
    public static Task<bool> AskForPhotosAgain()
    {
        var screen = Shown ?? throw new IOException("PhotoSense has to be open on the screen to ask.");
        var (id, answer) = Question();
        screen.RunOnUiThread(() => screen.RequestPermissions(PhotoPermissions, id));
        return answer.Task;
    }

    private static (int Id, TaskCompletionSource<bool> Answer) Question()
    {
        var id = Interlocked.Increment(ref _nextQuestion);
        var answer = Questions[id] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (id, answer);
    }

    /// <summary>Keeps the screen on while the phone offers photos to a computer: a phone that locks stops the app, and the sending with it.</summary>
    public static void KeepScreenOn(bool on)
    {
        if (Shown is not { } screen) return;
        screen.RunOnUiThread(() =>
        {
            if (on) screen.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            else screen.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        });
    }

    private sealed class UnderBars(MainActivity screen) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        /// <summary>Lets the page under the bars at the top and foot, telling it how tall they are; keeps it clear of those at the sides.</summary>
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            var density = view.Resources!.DisplayMetrics!.Density;
            view.SetPadding(bars.Left, 0, bars.Right, 0);
            screen._insets = (bars.Top / density, bars.Bottom / density);
            screen.TellInsets();
            return WindowInsets.Consumed;
        }
    }
}
