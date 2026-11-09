namespace PhotoSense.Domain.Repositories;

/// <summary>A phone or computer this one has been paired with, and the secret the two share since.</summary>
/// <param name="Id">The other device's own id.</param>
/// <param name="Name">What the other device is called, to show the person.</param>
/// <param name="Key">The secret the two devices share, from which each connection's keys are made.</param>
public sealed record PairedDevice(string Id, string Name, byte[] Key);

/// <summary>The devices this one has been paired with to send photos between them, and its own id among them.</summary>
public interface IPairingStore
{
    /// <summary>This device's own id, made once and kept.</summary>
    Task<string> SelfIdAsync(CancellationToken ct);

    /// <summary>The pairing with a device, or null when this one has never been paired with it.</summary>
    Task<PairedDevice?> GetAsync(string id, CancellationToken ct);

    /// <summary>Keeps a pairing, noting when it was made, in place of any earlier one with the same device.</summary>
    Task SaveAsync(PairedDevice device, CancellationToken ct);
}
