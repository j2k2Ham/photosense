namespace Trovolve.Domain.Repositories;

/// <summary>How a phone's copies are named on the computer.</summary>
public enum FileNaming
{
    /// <summary>As on the phone: "IMG_4821.HEIC".</summary>
    AsOnPhone,
    /// <summary>By when it was taken: "2026-03-02 14.31.07.HEIC".</summary>
    Date,
    /// <summary>By when and where it was taken: "2026-03-02 14.31.07 Big Sky.HEIC".</summary>
    DatePlace,
}

/// <summary>A phone that backs up to this computer, and how its backups stand.</summary>
/// <param name="Id">The phone's own id, as it was paired.</param>
/// <param name="Name">What the phone is called.</param>
/// <param name="Kind">What sort of device it is, as it says: "iPhone", "iPad", "Android phone" or "Android tablet"; null until it has said.</param>
/// <param name="Folder">The folder on this computer its backups go to.</param>
/// <param name="Paused">The person paused its backups on this computer: it is turned away until they resume.</param>
/// <param name="Paired">When it was paired with this computer.</param>
/// <param name="LastBackup">When it last finished a backup here.</param>
/// <param name="LastChecked">When the files kept for it were last checked against their checksums.</param>
/// <param name="Safe">How many of the phone's pictures and videos it said could be deleted from it, as their copies are here, after its keep rules.</param>
/// <param name="SafeBytes">How much those take on the phone.</param>
/// <param name="TripFolders">The trip folders made on this computer when the phone asked, where its files may be too; null for none.</param>
/// <param name="TripsFolder">The folder its trip folders are made in, as the person chose; null for the one beside the phones' folders, Pictures/Trovolve.</param>
/// <param name="Names">How its copies are named here, those arriving among them.</param>
public sealed record BackupPhone(string Id, string Name, string? Kind, string Folder, bool Paused, DateTimeOffset Paired, DateTimeOffset? LastBackup, DateTimeOffset? LastChecked, int Safe = 0, long SafeBytes = 0,
    IReadOnlyList<string>? TripFolders = null, string? TripsFolder = null, FileNaming Names = FileNaming.AsOnPhone);

/// <summary>A file this computer keeps for a phone, by its checksum.</summary>
/// <param name="PhoneId">The phone it came from.</param>
/// <param name="Sha256">The SHA-256 checksum of its bytes, in lower-case hex.</param>
/// <param name="Path">Where it is on this computer.</param>
/// <param name="Size">Its size when its checksum was taken.</param>
/// <param name="ModifiedUtc">When it last changed, as the disk said when its checksum was taken.</param>
/// <param name="Arrived">When it arrived.</param>
public sealed record HeldFile(string PhoneId, string Sha256, string Path, long Size, DateTime ModifiedUtc, DateTimeOffset Arrived);

/// <summary>Something that happened in a phone's backups, as the computer lists it.</summary>
/// <param name="PhoneId">The phone.</param>
/// <param name="At">When it happened.</param>
/// <param name="Text">What happened, in a sentence for the person: "214 items backed up and checked".</param>
public sealed record BackupEvent(string PhoneId, DateTimeOffset At, string Text);

/// <summary>What a computer keeps of the phones that back up to it: each phone, the files it holds for each, and what happened.</summary>
public interface IBackupStore
{
    /// <summary>Every phone that backs up to this computer, in the order they were paired.</summary>
    Task<IReadOnlyList<BackupPhone>> PhonesAsync(CancellationToken ct);

    /// <summary>Keeps a phone, in place of what was kept of it before.</summary>
    Task SavePhoneAsync(BackupPhone phone, CancellationToken ct);

    /// <summary>The files kept for a phone.</summary>
    Task<IReadOnlyList<HeldFile>> HeldAsync(string phoneId, CancellationToken ct);

    /// <summary>Keeps a file for its phone, in place of any kept before with the same checksum.</summary>
    Task HoldAsync(HeldFile file, CancellationToken ct);

    /// <summary>Forgets the files kept for a phone with these checksums: they are no longer there as they were.</summary>
    Task ForgetAsync(string phoneId, IReadOnlyCollection<string> sha256, CancellationToken ct);

    /// <summary>Notes what happened.</summary>
    Task NoteAsync(BackupEvent happened, CancellationToken ct);

    /// <summary>What happened to a phone's backups, the latest first.</summary>
    Task<IReadOnlyList<BackupEvent>> EventsAsync(string phoneId, int atMost, CancellationToken ct);
}

/// <summary>The computer a phone backs up to, as the phone keeps it.</summary>
/// <param name="ComputerId">The computer's own id, as it was paired.</param>
/// <param name="Name">What the computer is called.</param>
/// <param name="Folder">Where the computer puts this phone's backups, as it last said.</param>
/// <param name="LastChecked">When the computer last checked the files it keeps for this phone, and said which they are.</param>
/// <param name="LastBackup">When a backup last finished.</param>
/// <param name="Trips">Where the computer makes trip folders, as it last said ("Pictures/Trovolve"); null until it has said.</param>
/// <param name="Names">How the computer names this phone's copies, as it last said.</param>
public sealed record BackupComputer(string ComputerId, string Name, string Folder, DateTimeOffset? LastChecked, DateTimeOffset? LastBackup, string? Trips = null,
    FileNaming Names = FileNaming.AsOnPhone);

/// <summary>How a phone backs up by itself, and what Safe to delete keeps on it.</summary>
/// <param name="Automatic">Backs up by itself (Pro).</param>
/// <param name="OnlyWhileCharging">Backs up by itself only while the phone is charging.</param>
/// <param name="IncludeVideos">Backs up videos as well as pictures; a Live Photo's video always goes with its picture.</param>
/// <param name="KeepFavorites">Safe to delete leaves the person's favorites on the phone.</param>
/// <param name="KeepRecent">Safe to delete leaves what was taken in the last 30 days on the phone.</param>
public sealed record BackupSettings(bool Automatic, bool OnlyWhileCharging, bool IncludeVideos, bool KeepFavorites, bool KeepRecent)
{
    /// <summary>As a phone starts: automatic backup off, and on while charging, with videos, once it is turned on; favorites and the last 30 days kept.</summary>
    public static readonly BackupSettings Default = new(false, true, true, true, true);
}

/// <summary>
/// What a phone keeps of its backups: the computer they go to, how they run, the checksums of its files, and which of
/// those checksums the computer said it holds.
/// </summary>
public interface IBackupLedger
{
    /// <summary>The computer backups go to, or null when the phone has been paired with none.</summary>
    Task<BackupComputer?> ComputerAsync(CancellationToken ct);

    /// <summary>Keeps the computer backups go to; a computer other than the one before starts with nothing on it.</summary>
    Task SaveComputerAsync(BackupComputer computer, CancellationToken ct);

    /// <summary>How the phone backs up by itself.</summary>
    Task<BackupSettings> SettingsAsync(CancellationToken ct);

    /// <summary>Keeps how the phone backs up by itself.</summary>
    Task SaveSettingsAsync(BackupSettings settings, CancellationToken ct);

    /// <summary>The checksums of the files the computer holds for this phone, as it last said.</summary>
    Task<IReadOnlySet<string>> OnComputerAsync(CancellationToken ct);

    /// <summary>Keeps the checksums the computer holds: all of them in place of those before, or these as well.</summary>
    /// <param name="sha256">The checksums.</param>
    /// <param name="all">These are all it holds, in place of those kept before.</param>
    /// <param name="ct">Stops it before it starts.</param>
    Task KeepOnComputerAsync(IReadOnlyCollection<string> sha256, bool all, CancellationToken ct);

    /// <summary>A file's checksum, as worked out when it had this size and time; null when it has not been worked out so.</summary>
    Task<string?> ChecksumAsync(string path, long size, DateTime modifiedUtc, CancellationToken ct);

    /// <summary>Keeps a file's checksum, for as long as its size and time stay the same.</summary>
    Task KeepChecksumAsync(string path, long size, DateTime modifiedUtc, string sha256, CancellationToken ct);

    /// <summary>The trip folders the computer made when this phone asked, by their names; another computer starts with none.</summary>
    Task<IReadOnlySet<string>> FoldersMadeAsync(CancellationToken ct);

    /// <summary>Keeps that the computer made a trip folder.</summary>
    Task KeepFolderMadeAsync(string folder, CancellationToken ct);
}
