using Photos;
using PhotosUI;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>What the person let PhotoSense see of the photo library, and the system's own ways of letting it see more.</summary>
public sealed class PhotoKitAccess : IPhotoAccess
{
    public PhotoAccess Current => PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite) switch
    {
        PHAuthorizationStatus.Authorized => PhotoAccess.Everything,
        PHAuthorizationStatus.Limited => PhotoAccess.Selected,
        _ => PhotoAccess.Nothing,
    };

    public Task ChooseMoreAsync(CancellationToken ct)
    {
        var closed = new TaskCompletionSource();
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (PageViewController.Shown is not { } page) { closed.TrySetResult(); return; }
            // The system's own picker, already showing what was chosen before; the library reads what it allows next.
            PHPhotoLibrary.SharedPhotoLibrary.PresentLimitedLibraryPicker(page, _ => closed.TrySetResult());
        });
        return closed.Task.WaitAsync(ct);
    }

    public void OpenSettings() => UIApplication.SharedApplication.InvokeOnMainThread(() =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl(UIApplication.OpenSettingsUrlString), new UIApplicationOpenUrlOptions(), null));
}
