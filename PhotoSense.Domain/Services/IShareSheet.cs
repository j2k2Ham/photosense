namespace PhotoSense.Domain.Services;

/// <summary>A phone's own share sheet, which hands what is shared to the app the person picks.</summary>
public interface IShareSheet
{
    /// <summary>Shows the share sheet with a picture, a PNG of <paramref name="png"/>, named <paramref name="name"/>; done once it is shown.</summary>
    /// <exception cref="IOException">The phone could not show it.</exception>
    Task ShareImageAsync(byte[] png, string name, CancellationToken ct);
}
