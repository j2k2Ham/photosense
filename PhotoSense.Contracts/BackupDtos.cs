using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Transfer;

/// <summary>How a phone backs up by itself, and what Safe to delete keeps on it.</summary>
public sealed class BackupSettingsDto
{
    /// <summary>Backs up by itself (Pro).</summary>
    [JsonPropertyName("automatic")] public bool Automatic { get; init; }
    /// <summary>Only while the phone is charging.</summary>
    [JsonPropertyName("onlyWhileCharging")] public bool OnlyWhileCharging { get; init; }
    /// <summary>Videos too; a Live Photo's video always goes with its picture.</summary>
    [JsonPropertyName("includeVideos")] public bool IncludeVideos { get; init; }
    /// <summary>Safe to delete keeps favorites.</summary>
    [JsonPropertyName("keepFavorites")] public bool KeepFavorites { get; init; }
    /// <summary>Safe to delete keeps what was taken in the last 30 days.</summary>
    [JsonPropertyName("keepRecent")] public bool KeepRecent { get; init; }
}

/// <summary>A picture or video in the strip of those backing up.</summary>
public sealed class BackupTileDto
{
    /// <summary>Its id, as Library knows it: its preview is organize/files/{id}/thumbnail.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>It is on the computer, checked.</summary>
    [JsonPropertyName("done")] public bool Done { get; init; }
}

/// <summary>How backing up stands on the phone.</summary>
public sealed class BackupStateDto
{
    /// <summary>"unpaired", "ready", "running", "paused" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "unpaired";
    /// <summary>The computer backups go to.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>Where the computer puts them: "Pictures/PhotoSense/Alex's iPhone".</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
    /// <summary>Pictures and videos a backup would send.</summary>
    [JsonPropertyName("waiting")] public int Waiting { get; init; }
    /// <summary>How much those take.</summary>
    [JsonPropertyName("waitingBytes")] public long WaitingBytes { get; init; }
    /// <summary>Pictures and videos on the computer, checked.</summary>
    [JsonPropertyName("onComputer")] public int OnComputer { get; init; }
    /// <summary>Files of the backup going on, or paused, that are on the computer.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Files that backup sends.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Bytes of it sent.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Bytes it sends.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>When the computer last checked what it holds (ISO 8601).</summary>
    [JsonPropertyName("lastChecked")] public string? LastChecked { get; init; }
    /// <summary>When a backup last finished (ISO 8601).</summary>
    [JsonPropertyName("lastBackup")] public string? LastBackup { get; init; }
    /// <summary>A few of those being backed up, or waiting.</summary>
    [JsonPropertyName("strip")] public IReadOnlyList<BackupTileDto> Strip { get; init; } = [];
    /// <summary>Why the last backup stopped.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
    /// <summary>How the phone backs up by itself.</summary>
    [JsonPropertyName("settings")] public BackupSettingsDto Settings { get; init; } = new();
}

/// <summary>Which of the library's pictures and videos are on the computer, and which wait, by their ids.</summary>
public sealed class LibraryBackupDto
{
    /// <summary>The computer; null when the phone has been paired with none.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>The ids of those on the computer.</summary>
    [JsonPropertyName("onComputer")] public IReadOnlyList<string> OnComputer { get; init; } = [];
    /// <summary>The ids of those a backup would send.</summary>
    [JsonPropertyName("waiting")] public IReadOnlyList<string> Waiting { get; init; } = [];
    /// <summary>When the computer last checked (ISO 8601).</summary>
    [JsonPropertyName("lastChecked")] public string? LastChecked { get; init; }
}

/// <summary>A number of pictures and videos, and how much they take.</summary>
public sealed class ItemCountDto
{
    /// <summary>How many.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>How much they take.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>What could be deleted from the phone, as its copies are on the computer, and what the keep rules keep.</summary>
public sealed class SafeReviewDto
{
    /// <summary>The computer.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>Where the copies are on it.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
    /// <summary>When the computer last checked its copies (ISO 8601).</summary>
    [JsonPropertyName("lastChecked")] public string? LastChecked { get; init; }
    /// <summary>Every picture and video whose copy the computer holds.</summary>
    [JsonPropertyName("checked")] public ItemCountDto Checked { get; init; } = new();
    /// <summary>Those that are favorites.</summary>
    [JsonPropertyName("favorites")] public ItemCountDto Favorites { get; init; } = new();
    /// <summary>Those taken in the last 30 days.</summary>
    [JsonPropertyName("recent")] public ItemCountDto Recent { get; init; } = new();
    /// <summary>Those the keep rules leave to be deleted.</summary>
    [JsonPropertyName("removable")] public ItemCountDto Removable { get; init; } = new();
    /// <summary>Everything the phone can hold, in bytes.</summary>
    [JsonPropertyName("storageTotal")] public long StorageTotal { get; init; }
    /// <summary>What is free on it now, in bytes.</summary>
    [JsonPropertyName("storageFree")] public long StorageFree { get; init; }
    /// <summary>The phone's photos are kept in iCloud too.</summary>
    [JsonPropertyName("cloudPhotos")] public bool CloudPhotos { get; init; }
    /// <summary>The keep rules, among the rest.</summary>
    [JsonPropertyName("settings")] public BackupSettingsDto Settings { get; init; } = new();
}

/// <summary>A backup arriving from a phone now.</summary>
public sealed class ArrivingBackupDto
{
    /// <summary>Files arrived and checked.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Files the phone is sending.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Bytes arrived.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Bytes the phone is sending.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>When it started (ISO 8601).</summary>
    [JsonPropertyName("started")] public string Started { get; init; } = string.Empty;
    /// <summary>The ids of the files that arrived last, the latest last: each preview is organize/files/{id}/thumbnail.</summary>
    [JsonPropertyName("arrived")] public IReadOnlyList<string> Arrived { get; init; } = [];
}

/// <summary>Something that happened in a phone's backups.</summary>
public sealed class BackupEventDto
{
    /// <summary>When (ISO 8601).</summary>
    [JsonPropertyName("at")] public string At { get; init; } = string.Empty;
    /// <summary>What happened: "214 items backed up and checked".</summary>
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}

/// <summary>A phone that backs up to this computer, as Phones shows it.</summary>
public sealed class BackupPhoneDto
{
    /// <summary>The phone's own id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>What it is called.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>"iPhone", "iPad", "Android phone" or "Android tablet"; null until it has backed up.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    /// <summary>Where its backups go, as the person knows it.</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    /// <summary>The same folder as a full path, for the folder picker to start from.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    /// <summary>Its backups are paused on this computer.</summary>
    [JsonPropertyName("paused")] public bool Paused { get; init; }
    /// <summary>When it was paired (ISO 8601).</summary>
    [JsonPropertyName("paired")] public string Paired { get; init; } = string.Empty;
    /// <summary>When it last finished a backup (ISO 8601).</summary>
    [JsonPropertyName("lastBackup")] public string? LastBackup { get; init; }
    /// <summary>When its files were last checked (ISO 8601).</summary>
    [JsonPropertyName("lastChecked")] public string? LastChecked { get; init; }
    /// <summary>Files on this computer from it.</summary>
    [JsonPropertyName("held")] public int Held { get; init; }
    /// <summary>How much they take.</summary>
    [JsonPropertyName("heldBytes")] public long HeldBytes { get; init; }
    /// <summary>How many it said could be deleted from it, after its keep rules.</summary>
    [JsonPropertyName("safe")] public int Safe { get; init; }
    /// <summary>How much those take on it.</summary>
    [JsonPropertyName("safeBytes")] public long SafeBytes { get; init; }
    /// <summary>How many trip folders were made here when it asked.</summary>
    [JsonPropertyName("tripFolders")] public int TripFolders { get; init; }
    /// <summary>Where its trip folders are made, as the person knows it: "Pictures/PhotoSense".</summary>
    [JsonPropertyName("tripsFolder")] public string TripsFolder { get; init; } = string.Empty;
    /// <summary>The same, whole, for the folder picker to start from.</summary>
    [JsonPropertyName("tripsPath")] public string TripsPath { get; init; } = string.Empty;
    /// <summary>The backup arriving from it now, if any.</summary>
    [JsonPropertyName("arriving")] public ArrivingBackupDto? Arriving { get; init; }
    /// <summary>What happened, the latest first.</summary>
    [JsonPropertyName("events")] public IReadOnlyList<BackupEventDto> Events { get; init; } = [];
}

/// <summary>What to back up: the pictures and videos chosen (Send now), or everything that waits.</summary>
public sealed class BackupStartRequest
{
    /// <summary>The ids of the pictures and videos chosen, as Library knows them; none for everything that waits.</summary>
    [JsonPropertyName("files")] public IReadOnlyList<string>? Files { get; init; }
}

/// <summary>How many Safe to delete deletes.</summary>
public sealed class SafeRemoveRequest
{
    /// <summary>The most to delete, the largest first; none for all of them.</summary>
    [JsonPropertyName("atMost")] public int? AtMost { get; init; }
}

/// <summary>What Safe to delete deleted.</summary>
public sealed class SafeRemovalDto
{
    /// <summary>Pictures and videos sent to the phone's Recently Deleted or Trash.</summary>
    [JsonPropertyName("removed")] public int Removed { get; init; }
    /// <summary>How much they took.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Those it could have deleted that are still on the phone.</summary>
    [JsonPropertyName("left")] public int Left { get; init; }
    /// <summary>Everything the phone can hold, in bytes.</summary>
    [JsonPropertyName("storageTotal")] public long StorageTotal { get; init; }
    /// <summary>What was free on it before, in bytes.</summary>
    [JsonPropertyName("storageFreeBefore")] public long StorageFreeBefore { get; init; }
}

/// <summary>The folder a phone's backups go to from now on.</summary>
public sealed class BackupFolderRequest
{
    /// <summary>A folder on this computer.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
}

/// <summary>Whether a phone's backups are paused on this computer.</summary>
public sealed class BackupPauseRequest
{
    /// <summary>Paused, or running again.</summary>
    [JsonPropertyName("paused")] public bool Paused { get; init; }
}
