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

    /// <summary>Asks Android's question of what PhotoSense may see; done once it is answered. Android stops asking once the person has said no twice.</summary>
    public Task AskAsync(CancellationToken ct) => MainActivity.AskForPhotosAgain().WaitAsync(ct);

    /// <summary>Asks again for the photos PhotoSense may see; done once Android's question is put away.</summary>
    public Task ChooseMoreAsync(CancellationToken ct) => MainActivity.AskForPhotosAgain().WaitAsync(ct);

    /// <summary>Opens PhotoSense's page of Android's settings.</summary>
    public void OpenSettings()
    {
        var context = global::Android.App.Application.Context;
        context.StartActivity(new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings, global::Android.Net.Uri.Parse("package:" + context.PackageName))
            .AddFlags(ActivityFlags.NewTask));
    }
}
