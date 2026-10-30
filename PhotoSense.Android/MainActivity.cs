using System.Collections.Concurrent;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Window;
using Android.Widget;

namespace PhotoSense.AndroidApp;

/// <summary>The one screen: the page, the whole of it, between the phone's own bars.</summary>
[Activity(Name = "com.j2k2ham.photosense.MainActivity", Label = "PhotoSense", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : Activity
{
    private const int AskForPhotos = 1;
    private static int _nextQuestion = 100;
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> Questions = new();
    private WebView? _page;

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
        // The page fills the screen between the status bar and the bar of buttons, which Android draws over it.
        var screen = new FrameLayout(this);
        screen.AddView(page, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        screen.SetOnApplyWindowInsetsListener(new BetweenBars());
        SetContentView(screen);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault, new BackCallback(Back));

        // The page waits until the person has said what PhotoSense may see; GitHub's emulator has said so already.
        if (SelfCheck.Running || PhotoPermissions.All(p => CheckSelfPermission(p) == Permission.Granted)) page.LoadUrl(PageClient.Origin);
        else RequestPermissions(PhotoPermissions, AskForPhotos);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == AskForPhotos) _page?.LoadUrl(PageClient.Origin);
        else if (Questions.TryRemove(requestCode, out var asked)) asked.TrySetResult(grantResults.Any(g => g == Permission.Granted));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (Questions.TryRemove(requestCode, out var asked)) asked.TrySetResult(resultCode == Result.Ok);
    }

    // Back puts away the window on top of the page, as Esc does on a computer; with none open, it leaves the app.
    private void Back() => _page?.EvaluateJavascript(
        "(() => { const open = document.querySelector('[role=dialog], [role=alertdialog], [role=menu]'); if (open) window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); return !!open; })()",
        new Answered(put => { if (!put) RunOnUiThread(Finish); }));

#pragma warning disable CA1422 // Before Android 13, the only way to hear the back button.
    public override void OnBackPressed() => Back();
#pragma warning restore CA1422

    private sealed class Answered(Action<bool> then) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => then(value?.ToString() == "true");
    }

    private sealed class BackCallback(Action back) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked() => back();
    }

    protected override void OnDestroy()
    {
        if (Shown == this) Shown = null;
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

    private sealed class BetweenBars : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            return WindowInsets.Consumed;
        }
    }
}
