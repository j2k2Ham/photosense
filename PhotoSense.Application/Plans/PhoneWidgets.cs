using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Plans;

/// <summary>
/// The phone's home screen widgets (35): the Storage widget, how full the phone is, what Free up space can free and
/// how backing up stands; and the Swipe to clean widget, how many of today's set are left, the week and the streak.
/// The app hands them what to show as it goes to the background, and when the phone wakes it to back up or check
/// its storage. They are free.
/// </summary>
public sealed class PhoneWidgets
{
    private readonly IWidgets _widgets;
    private readonly IDeviceStatus _device;
    private readonly CleanUpCategories _categories;
    private readonly PhoneBackup _backup;
    private readonly SwipeToClean _swipe;

    /// <summary>The widgets, and where what they show comes from.</summary>
    /// <param name="widgets">The phone's widgets.</param>
    /// <param name="device">How full the phone's storage is.</param>
    /// <param name="categories">What Free up space can free.</param>
    /// <param name="backup">How backing up stands.</param>
    /// <param name="swipe">Today's set to swipe, the week and the streak.</param>
    public PhoneWidgets(IWidgets widgets, IDeviceStatus device, CleanUpCategories categories, PhoneBackup backup, SwipeToClean swipe)
    {
        _widgets = widgets;
        _device = device;
        _categories = categories;
        _backup = backup;
        _swipe = swipe;
    }

    /// <summary>What the widgets show now.</summary>
    public async Task<WidgetSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var storage = _device.Storage;
        var full = storage.Total > 0 ? (int)Math.Round(100d * (storage.Total - storage.Free) / storage.Total) : 0;
        var (_, freeable) = await _categories.FreeUpSpaceAsync(ct);
        var backup = await _backup.StateAsync(ct);
        var swipe = await _swipe.StateAsync(ct);
        var ready = swipe.Set.Count(c => c.Delete is null);
        return new WidgetSnapshot(full, freeable == 0 ? "0 KB" : Sizes.Of(freeable), BackupLine(backup), backup.Computer is null ? "Pair" : "Back up now",
            ready, ReadyLine(ready, swipe), [.. swipe.Week.Select(d => d.Swiped)], swipe.Streak > 0 ? $"{swipe.Streak}-day streak" : "Swipe today to start a streak");
    }

    /// <summary>Hands the widgets what to show now.</summary>
    public async Task RefreshAsync(CancellationToken ct) => _widgets.Show(await SnapshotAsync(ct));

    private static string BackupLine(BackupState backup)
    {
        if (backup.Computer is not { } computer) return "Pair your computer to back up";
        if (backup.Stage == BackupStage.Running) return $"Backing up to {computer}…";
        return backup.Waiting == 0 ? $"All on {computer}" : $"{(backup.Waiting == 1 ? "1 item" : $"{backup.Waiting:N0} items")} waiting";
    }

    private static string ReadyLine(int ready, SwipeState swipe)
        => ready > 0 ? (ready == 1 ? "photo ready today" : "photos ready today") : swipe.Unswiped == 0 && swipe.Set.Count == 0 ? "Every photo swiped" : "Done for today";
}
