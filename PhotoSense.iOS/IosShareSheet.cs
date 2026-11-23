using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>iOS's share sheet, shown over the page with a card or a photo; on an iPad it points at the middle of the screen.</summary>
public sealed class IosShareSheet : IShareSheet
{
    /// <inheritdoc />
    public Task ShareImageAsync(byte[] png, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var image = UIImage.LoadFromData(NSData.FromArray(png)) ?? throw new IOException("The card could not be made into a picture.");
        return ShowAsync(image);
    }

    /// <inheritdoc />
    public Task ShareFileAsync(string file, string mimeType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Given as the file itself, so that what it is shared to takes its name and kind from it.
        return ShowAsync(NSUrl.FromFilename(file));
    }

    // The share sheet with what is shared, over the page; done once it is shown.
    private static Task ShowAsync(NSObject shared)
    {
        var page = PageViewController.Shown ?? throw new IOException("PhotoSense has to be open on the screen to share.");
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.InvokeOnMainThread(() =>
        {
            var sheet = new UIActivityViewController([shared], null);
            // An iPad shows it as a popover, which must point somewhere: the middle of the page.
            if (sheet.PopoverPresentationController is { } popover && page.View is { } view)
            {
                popover.SourceView = view;
                popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
            }
            page.PresentViewController(sheet, true, shown.SetResult);
        });
        return shown.Task;
    }
}
