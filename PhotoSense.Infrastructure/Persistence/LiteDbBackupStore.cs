using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The phones that back up to this computer, the files kept for each, and what happened, in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbBackupStore : IBackupStore
{
    private readonly ILiteCollection<PhoneDocument> _phones;
    private readonly ILiteCollection<HeldDocument> _held;
    private readonly ILiteCollection<EventDocument> _events;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbBackupStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<PhoneDocument>();
        LiteDbMapping.Prepare<HeldDocument>();
        LiteDbMapping.Prepare<EventDocument>();
        _phones = db.GetCollection<PhoneDocument>("backup_phones");
        _held = db.GetCollection<HeldDocument>("backup_held");
        _held.EnsureIndex(d => d.PhoneId);
        _events = db.GetCollection<EventDocument>("backup_events");
        _events.EnsureIndex(d => d.PhoneId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupPhone>> PhonesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BackupPhone>>([.. _phones.FindAll().OrderBy(d => d.PairedUtcTicks).Select(d =>
            new BackupPhone(d.Id, d.Name, d.Kind, d.Folder, d.Paused, Time(d.PairedUtcTicks), Time(d.LastBackupUtcTicks), Time(d.LastCheckedUtcTicks), d.Safe, d.SafeBytes,
                d.TripFolders is { Count: > 0 } trips ? trips : null, d.TripsFolder, (FileNaming)d.Names))]);
    }

    /// <inheritdoc />
    public Task SavePhoneAsync(BackupPhone phone, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _phones.Upsert(new PhoneDocument
        {
            Id = phone.Id, Name = phone.Name, Kind = phone.Kind, Folder = phone.Folder, Paused = phone.Paused, PairedUtcTicks = phone.Paired.UtcTicks,
            LastBackupUtcTicks = phone.LastBackup?.UtcTicks, LastCheckedUtcTicks = phone.LastChecked?.UtcTicks, Safe = phone.Safe, SafeBytes = phone.SafeBytes, TripFolders = phone.TripFolders?.ToList(), TripsFolder = phone.TripsFolder, Names = (int)phone.Names,
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HeldFile>> HeldAsync(string phoneId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<HeldFile>>([.. _held.Find(d => d.PhoneId == phoneId).Select(d =>
            new HeldFile(d.PhoneId, d.Sha256, d.Path, d.Size, new DateTime(d.ModifiedUtcTicks, DateTimeKind.Utc), Time(d.ArrivedUtcTicks)))]);
    }

    /// <inheritdoc />
    public Task HoldAsync(HeldFile file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _held.Upsert(new HeldDocument
        {
            Id = Key(file.PhoneId, file.Sha256), PhoneId = file.PhoneId, Sha256 = file.Sha256, Path = file.Path, Size = file.Size,
            ModifiedUtcTicks = file.ModifiedUtc.Ticks, ArrivedUtcTicks = file.Arrived.UtcTicks,
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ForgetAsync(string phoneId, IReadOnlyCollection<string> sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var sha in sha256) _held.Delete(Key(phoneId, sha));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NoteAsync(BackupEvent happened, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _events.Insert(new EventDocument { PhoneId = happened.PhoneId, AtUtcTicks = happened.At.UtcTicks, Text = happened.Text });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupEvent>> EventsAsync(string phoneId, int atMost, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BackupEvent>>([.. _events.Find(d => d.PhoneId == phoneId)
            .OrderByDescending(d => d.AtUtcTicks).ThenByDescending(d => d.Id).Take(atMost).Select(d => new BackupEvent(d.PhoneId, Time(d.AtUtcTicks), d.Text))]);
    }

    private static string Key(string phoneId, string sha256) => $"{phoneId}/{sha256}";

    private static DateTimeOffset Time(long utcTicks) => new(utcTicks, TimeSpan.Zero);

    private static DateTimeOffset? Time(long? utcTicks) => utcTicks is { } t ? Time(t) : null;

    private sealed class PhoneDocument
    {
        /// <summary>The phone's own id.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>What it is called.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>What sort of device it is.</summary>
        public string? Kind { get; set; }
        /// <summary>Where its backups go.</summary>
        public string Folder { get; set; } = string.Empty;
        /// <summary>Its backups are paused on this computer.</summary>
        public bool Paused { get; set; }
        /// <summary>When it was paired, in UTC ticks.</summary>
        public long PairedUtcTicks { get; set; }
        /// <summary>When it last finished a backup, in UTC ticks.</summary>
        public long? LastBackupUtcTicks { get; set; }
        /// <summary>When its files were last checked, in UTC ticks.</summary>
        public long? LastCheckedUtcTicks { get; set; }
        /// <summary>How many it said could be deleted from it.</summary>
        public int Safe { get; set; }
        /// <summary>How much those take on it.</summary>
        public long SafeBytes { get; set; }
        /// <summary>The trip folders made when it asked.</summary>
        public List<string>? TripFolders { get; set; }
        /// <summary>Where its trip folders are made, as the person chose.</summary>
        public string? TripsFolder { get; set; }
        /// <summary>How its copies are named here.</summary>
        public int Names { get; set; }
    }

    private sealed class HeldDocument
    {
        /// <summary>The phone's id and the checksum: "phone/sha256".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The phone it came from.</summary>
        public string PhoneId { get; set; } = string.Empty;
        /// <summary>Its checksum.</summary>
        public string Sha256 { get; set; } = string.Empty;
        /// <summary>Where it is.</summary>
        public string Path { get; set; } = string.Empty;
        /// <summary>Its size when its checksum was taken.</summary>
        public long Size { get; set; }
        /// <summary>When it last changed, in UTC ticks, when its checksum was taken.</summary>
        public long ModifiedUtcTicks { get; set; }
        /// <summary>When it arrived, in UTC ticks.</summary>
        public long ArrivedUtcTicks { get; set; }
    }

    private sealed class EventDocument
    {
        /// <summary>LiteDB's own id, which orders what happened at the same moment.</summary>
        public int Id { get; set; }
        /// <summary>The phone.</summary>
        public string PhoneId { get; set; } = string.Empty;
        /// <summary>When it happened, in UTC ticks.</summary>
        public long AtUtcTicks { get; set; }
        /// <summary>What happened.</summary>
        public string Text { get; set; } = string.Empty;
    }
}

/// <summary>A phone's own record of its backups, in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbBackupLedger : IBackupLedger
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ComputerDocument> _computer;
    private readonly ILiteCollection<SettingsDocument> _settings;
    private readonly ILiteCollection<OnComputerDocument> _onComputer;
    private readonly ILiteCollection<ChecksumDocument> _checksums;
    private readonly ILiteCollection<FolderMadeDocument> _foldersMade;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbBackupLedger(LiteDatabase db)
    {
        LiteDbMapping.Prepare<ComputerDocument>();
        LiteDbMapping.Prepare<FolderMadeDocument>();
        LiteDbMapping.Prepare<SettingsDocument>();
        LiteDbMapping.Prepare<OnComputerDocument>();
        LiteDbMapping.Prepare<ChecksumDocument>();
        _db = db;
        _computer = db.GetCollection<ComputerDocument>("backup_computer");
        _settings = db.GetCollection<SettingsDocument>("backup_settings");
        _onComputer = db.GetCollection<OnComputerDocument>("backup_on_computer");
        _checksums = db.GetCollection<ChecksumDocument>("backup_checksums");
        _foldersMade = db.GetCollection<FolderMadeDocument>("backup_trip_folders");
    }

    /// <inheritdoc />
    public Task<BackupComputer?> ComputerAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_computer.FindById("computer") is { } d
            ? new BackupComputer(d.ComputerId, d.Name, d.Folder, Time(d.LastCheckedUtcTicks), Time(d.LastBackupUtcTicks), d.Trips, (FileNaming)d.Names)
            : null);
    }

    /// <inheritdoc />
    public Task SaveComputerAsync(BackupComputer computer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_computer)
        {
            var document = new ComputerDocument
            {
                Id = "computer", ComputerId = computer.ComputerId, Name = computer.Name, Folder = computer.Folder,
                LastCheckedUtcTicks = computer.LastChecked?.UtcTicks, LastBackupUtcTicks = computer.LastBackup?.UtcTicks, Trips = computer.Trips, Names = (int)computer.Names,
            };
            // Another computer holds none of what the one before did.
            if (_computer.FindById("computer") is { } before && before.ComputerId != computer.ComputerId)
                _db.Together(() =>
                {
                    _onComputer.DeleteAll();
                    _foldersMade.DeleteAll();
                    _computer.Upsert(document);
                });
            else _computer.Upsert(document);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BackupSettings> SettingsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_settings.FindById("settings") is { } d ? new BackupSettings(d.Automatic, d.OnlyWhileCharging, d.IncludeVideos, d.KeepFavorites, d.KeepRecent) : BackupSettings.Default);
    }

    /// <inheritdoc />
    public Task SaveSettingsAsync(BackupSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _settings.Upsert(new SettingsDocument { Id = "settings", Automatic = settings.Automatic, OnlyWhileCharging = settings.OnlyWhileCharging, IncludeVideos = settings.IncludeVideos, KeepFavorites = settings.KeepFavorites, KeepRecent = settings.KeepRecent });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> OnComputerAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<string>>(_onComputer.FindAll().Select(d => d.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task KeepOnComputerAsync(IReadOnlyCollection<string> sha256, bool all, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var documents = sha256.Select(sha => new OnComputerDocument { Id = sha }).ToList();
        lock (_onComputer)
            _db.Together(() =>
            {
                if (all) _onComputer.DeleteAll();
                _onComputer.Upsert(documents);
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ChecksumAsync(string path, long size, DateTime modifiedUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_checksums.FindById(Key(path)) is { } d && d.Size == size && d.ModifiedUtcTicks == modifiedUtc.Ticks ? d.Sha256 : null);
    }

    /// <inheritdoc />
    public Task KeepChecksumAsync(string path, long size, DateTime modifiedUtc, string sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _checksums.Upsert(new ChecksumDocument { Id = Key(path), Size = size, ModifiedUtcTicks = modifiedUtc.Ticks, Sha256 = sha256 });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> FoldersMadeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<string>>(_foldersMade.FindAll().Select(d => d.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task KeepFolderMadeAsync(string folder, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _foldersMade.Upsert(new FolderMadeDocument { Id = folder });
        return Task.CompletedTask;
    }

    // As the phone's own paths are compared: two spellings of one path are one file.
    private static string Key(string path) => Domain.ValueObjects.PhotoPath.Key(path);

    private static DateTimeOffset? Time(long? utcTicks) => utcTicks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    private sealed class ComputerDocument
    {
        /// <summary>The one computer: "computer".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The computer's own id.</summary>
        public string ComputerId { get; set; } = string.Empty;
        /// <summary>What it is called.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Where it puts this phone's backups.</summary>
        public string Folder { get; set; } = string.Empty;
        /// <summary>When it last checked, in UTC ticks.</summary>
        public long? LastCheckedUtcTicks { get; set; }
        /// <summary>When a backup last finished, in UTC ticks.</summary>
        public long? LastBackupUtcTicks { get; set; }
        /// <summary>Where it makes trip folders.</summary>
        public string? Trips { get; set; }
        /// <summary>How it names this phone's copies.</summary>
        public int Names { get; set; }
    }

    private sealed class SettingsDocument
    {
        /// <summary>The one record of settings: "settings".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>Backs up by itself.</summary>
        public bool Automatic { get; set; }
        /// <summary>Only while charging.</summary>
        public bool OnlyWhileCharging { get; set; }
        /// <summary>Videos too.</summary>
        public bool IncludeVideos { get; set; }
        /// <summary>Safe to delete keeps favorites.</summary>
        public bool KeepFavorites { get; set; }
        /// <summary>Safe to delete keeps the last 30 days.</summary>
        public bool KeepRecent { get; set; }
    }

    private sealed class OnComputerDocument
    {
        /// <summary>A checksum the computer holds.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
    }

    private sealed class FolderMadeDocument
    {
        /// <summary>The trip folder's name.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
    }

    private sealed class ChecksumDocument
    {
        /// <summary>The file's path, as compared.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>Its size when its checksum was worked out.</summary>
        public long Size { get; set; }
        /// <summary>When it last changed, in UTC ticks, when its checksum was worked out.</summary>
        public long ModifiedUtcTicks { get; set; }
        /// <summary>Its checksum.</summary>
        public string Sha256 { get; set; } = string.Empty;
    }
}
