namespace PhotoSense.Domain.Repositories;

/// <summary>A phone or computer this one has been paired with, and the secret the two share since.</summary>
public sealed record PairedDevice(string Id, string Name, byte[] Key, DateTime PairedUtc);

/// <summary>The devices this one has been paired with to send photos between them, and its own id among them.</summary>
public interface IPairingStore
{
    /// <summary>This device's own id, made once and kept.</summary>
    Task<string> SelfIdAsync();

    Task<PairedDevice?> GetAsync(string id);

    /// <summary>Keeps a pairing, in place of any earlier one with the same device.</summary>
    Task SaveAsync(PairedDevice device);
}
