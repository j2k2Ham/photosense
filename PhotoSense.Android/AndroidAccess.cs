using Android;
using Android.Content;
using Android.Content.PM;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>What the person let PhotoSense see of the phone's photos, and Android's own ways of letting it see more.</summary>
public sealed class AndroidAccess : IPhotoAccess
{
    public PhotoAccess Current
    {
        get
        {
            var context = global::Android.App.Application.Context;
            bool Has(string permission) => context.CheckSelfPermission(permission) == Permission.Granted;
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Has(Manifest.Permission.ReadExternalStorage) ? PhotoAccess.Everything : PhotoAccess.Nothing;
            if (Has(Manifest.Permission.ReadMediaImages)) return PhotoAccess.Everything;
            // Android 14 lets the person pick the photos PhotoSense sees.
            return OperatingSystem.IsAndroidVersionAtLeast(34) && Has(Manifest.Permission.ReadMediaVisualUserSelected) ? PhotoAccess.Selected : PhotoAccess.Nothing;
        }
    }

    public Task ChooseMoreAsync(CancellationToken ct) => MainActivity.AskForPhotosAgain().WaitAsync(ct);

    public void OpenSettings()
    {
        var context = global::Android.App.Application.Context;
        context.StartActivity(new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings, global::Android.Net.Uri.Parse("package:" + context.PackageName))
            .AddFlags(ActivityFlags.NewTask));
    }
}
