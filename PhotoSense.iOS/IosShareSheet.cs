using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>iOS's share sheet, shown over the page with the card as a picture; on an iPad it points at the middle of the screen.</summary>
public sealed class IosShareSheet : IShareSheet
{
    /// <inheritdoc />
    public Task ShareImageAsync(byte[] png, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var page = PageViewController.Shown ?? throw new IOException("PhotoSense has to be open on the screen to share.");
        var image = UIImage.LoadFromData(NSData.FromArray(png)) ?? throw new IOException("The card could not be made into a picture.");
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.InvokeOnMainThread(() =>
        {
            var sheet = new UIActivityViewController([image], null);
            if (sheet.PopoverPresentationController is { } popover)
            {
                popover.SourceView = page.View;
                popover.SourceRect = new CGRect(page.View!.Bounds.GetMidX(), page.View.Bounds.GetMidY(), 0, 0);
            }
            page.PresentViewController(sheet, true, shown.SetResult);
        });
        return shown.Task;
    }
}
