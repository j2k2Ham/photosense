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

    /// <summary>Marks the picture or video at <paramref name="path"/> a favorite in the phone's own photos app, or takes the mark away.</summary>
    /// <exception cref="IOException">It is not there, the person said no, or the phone could not change it.</exception>
    void SetFavorite(string path, bool favorite);
}

/// <summary>A copy of a picture or video made for sharing, in the app's own folder, which the app deletes once shared.</summary>
/// <param name="Path">Where the copy is.</param>
/// <param name="MimeType">What it is: "image/jpeg", "video/quicktime".</param>
public sealed record SharedCopy(string Path, string MimeType);

/// <summary>Copies of a phone's pictures and videos to share, with where they were taken and the camera's details taken out.</summary>
public interface ISharingCopies
{
    /// <summary>
    /// A copy of the picture or video at <paramref name="path"/>, without the place it was taken when
    /// <paramref name="removeLocation"/>, and without the camera's make, model, lens and exposure when
    /// <paramref name="removeCamera"/>. The original is left as it is.
    /// </summary>
    /// <exception cref="IOException">It is not there, or the phone could not make the copy.</exception>
    Task<SharedCopy> CopyForSharingAsync(string path, bool removeLocation, bool removeCamera, CancellationToken ct);
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
