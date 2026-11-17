namespace PhotoSense.Domain.Services;

/// <summary>How much a device holds, and how much of it is free, in bytes.</summary>
/// <param name="Total">Everything the device can hold.</param>
/// <param name="Free">What is free now.</param>
public sealed record DeviceStorage(long Total, long Free);

/// <summary>The pictures and videos the person marked as favorites in the phone's own photos app.</summary>
public interface IFavoritePhotos
{
    /// <summary>The paths, as the phone's file system shows them, of every favorite.</summary>
    Task<IReadOnlySet<string>> FavoritesAsync(CancellationToken ct);
}

/// <summary>What a phone says of itself: what sort it is, whether it is charging, its storage, and whether its photos are also kept in iCloud.</summary>
public interface IDeviceStatus
{
    /// <summary>What sort of device it is, as a computer lists it: "iPhone", "iPad", "Android phone" or "Android tablet".</summary>
    string Kind { get; }

    /// <summary>Whether the phone is charging now.</summary>
    bool Charging { get; }

    /// <summary>The phone's storage now.</summary>
    DeviceStorage Storage { get; }

    /// <summary>Whether the phone's photos are kept in iCloud too, so that deleting one deletes it from the person's other Apple devices.</summary>
    bool CloudPhotos { get; }
}
