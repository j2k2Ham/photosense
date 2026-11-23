using Foundation;
using PhotoSense.Domain.Services;
using UIKit;

namespace PhotoSense.iOS;

/// <summary>
/// What the iPhone or iPad says of itself: its sort, whether it is charging, its storage, and whether it is signed in
/// to iCloud. iOS does not tell an app whether iCloud Photos is on, so being signed in to iCloud stands for it, and the
/// page words its warning for that.
/// </summary>
public sealed class IosDevice : IDeviceStatus
{
    /// <summary>Starts watching the battery, which iOS reports only to an app that asks to watch it.</summary>
    public IosDevice() => UIDevice.CurrentDevice.BatteryMonitoringEnabled = true;

    /// <inheritdoc />
    public string Kind => UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad ? "iPad" : "iPhone";

    /// <inheritdoc />
    public bool Charging => UIDevice.CurrentDevice.BatteryState is UIDeviceBatteryState.Charging or UIDeviceBatteryState.Full;

    /// <inheritdoc />
    public DeviceStorage Storage
    {
        get
        {
            var attributes = NSFileManager.DefaultManager.GetFileSystemAttributes(NSFileManager.HomeDirectory ?? "/", out _);
            return attributes is null ? new DeviceStorage(0, 0) : new DeviceStorage((long)attributes.Size, (long)attributes.FreeSize);
        }
    }

    /// <inheritdoc />
    public bool CloudPhotos => NSFileManager.DefaultManager.UbiquityIdentityToken is not null;
}
