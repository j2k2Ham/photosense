using Photos;
using PhotosUI;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>What the person let PhotoSense see of the photo library, and the system's own ways of letting it see more.</summary>
public sealed class PhotoKitAccess : IPhotoAccess
{
    /// <summary>What the person has let PhotoSense see of the library.</summary>
    public PhotoAccess Current => PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite) switch
    {
        PHAuthorizationStatus.Authorized => PhotoAccess.Everything,
        PHAuthorizationStatus.Limited => PhotoAccess.Selected,
        _ => PhotoAccess.Nothing,
    };

    /// <summary>Shows Apple's picker for choosing more photos; done once it is put away.</summary>
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

    /// <summary>Opens PhotoSense's page of the Settings app.</summary>
    public void OpenSettings() => UIApplication.SharedApplication.InvokeOnMainThread(() =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl(UIApplication.OpenSettingsUrlString), new UIApplicationOpenUrlOptions(), null));
}
