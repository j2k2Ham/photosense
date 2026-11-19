namespace PhotoSense.Domain.Services;

/// <summary>
/// Where a phone keeps the hidden vault's recovery key, from which its key is made: the iPhone's Keychain, or a file
/// sealed by the Android Keystore. It never leaves the phone, and nothing PhotoSense sends carries it.
/// </summary>
public interface IVaultKeyStore
{
    /// <summary>The recovery key kept on this phone; null before the vault is turned on.</summary>
    Task<string?> RecoveryKeyAsync(CancellationToken ct);

    /// <summary>Keeps the recovery key, in place of any kept before.</summary>
    Task SaveRecoveryKeyAsync(string recoveryKey, CancellationToken ct);
}

/// <summary>The phone's own question before the hidden vault opens: Face ID, a fingerprint, or the phone's passcode.</summary>
public interface IUnlocker
{
    /// <summary>Asks the person to prove it is them; false when they cancel or cannot.</summary>
    /// <param name="reason">What the question says it is for.</param>
    /// <param name="ct">Stops the waiting; the question counts as not answered.</param>
    Task<bool> UnlockAsync(string reason, CancellationToken ct);
}

/// <summary>A file of the hidden vault, as it is kept.</summary>
/// <param name="Name">Its name in the vault.</param>
/// <param name="Length">Its size.</param>
/// <param name="ModifiedUtc">When it was written.</param>
public sealed record VaultFileInfo(string Name, long Length, DateTime ModifiedUtc);

/// <summary>
/// The hidden vault's own files, inside the app where no other app sees them: each an item sealed with the vault's
/// key. They are PhotoSense's own encrypted copies, not photos of the library.
/// </summary>
public interface IVaultFiles
{
    /// <summary>Every file in the vault.</summary>
    IReadOnlyList<VaultFileInfo> List();

    /// <summary>
    /// Writes a new file, under a name no file has yet, through <paramref name="write"/>; it is in the vault only once
    /// written whole. Should writing fail, nothing of it stays.
    /// </summary>
    /// <exception cref="IOException">A file of that name is there already, or the phone is full.</exception>
    Task WriteAsync(string name, Func<Stream, Task> write, CancellationToken ct);

    /// <summary>A file to read, from any place in it.</summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    Stream OpenRead(string name);

    /// <summary>Deletes a file of the vault: the item it holds is in the library again, or was never taken out.</summary>
    void Delete(string name);
}

/// <summary>A file of a picture or video to put in the phone's library.</summary>
/// <param name="Name">The name it was taken with.</param>
/// <param name="Content">What it holds.</param>
public sealed record ImportedFile(string Name, Stream Content);

/// <summary>Puts a picture or video in the phone's own library as a new item, as Photos or the gallery would.</summary>
public interface IPhotoImport
{
    /// <summary>
    /// Adds one picture or video, its files together (a Live Photo's picture and video), dated and placed as given.
    /// Returns the paths of its files in the library now.
    /// </summary>
    /// <param name="folder">Where it was, which the phone keeps it in again where it can.</param>
    /// <param name="files">Its files, the picture or video first.</param>
    /// <param name="taken">When it was taken; null to leave the phone's own.</param>
    /// <param name="place">Where it was taken; null for nowhere.</param>
    /// <param name="ct">Stops it before the phone has it.</param>
    /// <exception cref="IOException">The phone could not take it, or is full.</exception>
    /// <exception cref="UnauthorizedAccessException">PhotoSense may not add to the library.</exception>
    Task<IReadOnlyList<string>> ImportAsync(string folder, IReadOnlyList<ImportedFile> files, DateTime? taken, PlaceChange? place, CancellationToken ct);
}
