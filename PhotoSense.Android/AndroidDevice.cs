using Android.Content;
using Android.OS;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>
/// What the Android phone or tablet says of itself: its sort (a tablet is at least 600 dp across its narrower side),
/// whether it is charging, and its storage. Android keeps no photos in iCloud.
/// </summary>
/// <param name="context">The app, to ask the battery and the screen.</param>
public sealed class AndroidDevice(Context context) : IDeviceStatus
{
    /// <inheritdoc />
    public string Kind => context.Resources?.Configuration?.SmallestScreenWidthDp >= 600 ? "Android tablet" : "Android phone";

    /// <inheritdoc />
    public bool Charging => context.GetSystemService(Context.BatteryService) is BatteryManager battery && battery.IsCharging;

    /// <inheritdoc />
    public DeviceStorage Storage
    {
        get
        {
            var data = new StatFs(global::Android.OS.Environment.DataDirectory!.AbsolutePath);
            return new DeviceStorage(data.TotalBytes, data.AvailableBytes);
        }
    }

    /// <inheritdoc />
    public bool CloudPhotos => false;
}
