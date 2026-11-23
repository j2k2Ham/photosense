using Android.Content;
using AndroidX.Core.Content;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>Android's share sheet: a card or a photo's copy is kept in the app's cache and handed over through its file provider, readable only by the app picked.</summary>
public sealed class AndroidShareSheet(Context context) : IShareSheet
{
    /// <summary>The file provider's name, as the manifest gives it.</summary>
    public const string Authority = "com.j2k2ham.photosense.files";

    /// <inheritdoc />
    public async Task ShareImageAsync(byte[] png, string name, CancellationToken ct)
    {
        var screen = MainActivity.Shown ?? throw new IOException("PhotoSense has to be open on the screen to share.");
        var folder = Directory.CreateDirectory(Path.Combine(context.CacheDir!.AbsolutePath, "shared"));
        var file = Path.Combine(folder.FullName, Path.GetFileName(name));
        await File.WriteAllBytesAsync(file, png, ct);
        var uri = FileProvider.GetUriForFile(context, Authority, new Java.IO.File(file));
        var send = new Intent(Intent.ActionSend).SetType("image/png")!.PutExtra(Intent.ExtraStream, uri)!.AddFlags(ActivityFlags.GrantReadUriPermission)!;
        screen.RunOnUiThread(() => screen.StartActivity(Intent.CreateChooser(send, (string?)null)));
    }

    /// <inheritdoc />
    public Task ShareFileAsync(string file, string mimeType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var screen = MainActivity.Shown ?? throw new IOException("PhotoSense has to be open on the screen to share.");
        var uri = FileProvider.GetUriForFile(context, Authority, new Java.IO.File(file));
        var send = new Intent(Intent.ActionSend).SetType(mimeType)!.PutExtra(Intent.ExtraStream, uri)!.AddFlags(ActivityFlags.GrantReadUriPermission)!;
        screen.RunOnUiThread(() => screen.StartActivity(Intent.CreateChooser(send, (string?)null)));
        return Task.CompletedTask;
    }
}
