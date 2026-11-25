using LiteDB;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Persistence;

/// <summary>A phone's backup locations, which files each holds, checked, and the Safe to delete rule, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbLocationStore : ILocationStore
{
    private const string Rule = "count-at";
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<LocationDocument> _locations;
    private readonly ILiteCollection<CopyDocument> _copies;
    private readonly ILiteCollection<RuleDocument> _rules;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbLocationStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<LocationDocument>();
        LiteDbMapping.Prepare<CopyDocument>();
        LiteDbMapping.Prepare<RuleDocument>();
        _db = db;
        _locations = db.GetCollection<LocationDocument>("locations");
        _copies = db.GetCollection<CopyDocument>("location_copies");
        _copies.EnsureIndex(c => c.Location);
        _rules = db.GetCollection<RuleDocument>("location_rules");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupLocation>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BackupLocation>>([.. _locations.FindAll().Select(d => new BackupLocation(d.Id, d.Kind, d.Name, d.On, d.Account,
            d.Settings.ToDictionary(StringComparer.Ordinal), d.LastUsed, d.LastBackup, d.LastChecked, d.Problem))]);
    }

    /// <inheritdoc />
    public Task SaveAsync(BackupLocation location, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _locations.Upsert(new LocationDocument
        {
            Id = location.Id, Kind = location.Kind, Name = location.Name, On = location.On, Account = location.Account,
            Settings = location.Settings.ToDictionary(StringComparer.Ordinal), LastUsed = location.LastUsed, LastBackup = location.LastBackup,
            LastChecked = location.LastChecked, Problem = location.Problem,
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LiteDbTransaction.Together(_db, () =>
        {
            _locations.Delete(id);
            _copies.DeleteMany(c => c.Location == id);
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, LocationCopy>> CopiesAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<string, LocationCopy>>(_copies.Find(c => c.Location == id)
            .ToDictionary(c => c.Sha256, c => new LocationCopy(c.Name, c.Checksum is null ? null : new RemoteChecksum(c.ChecksumKind, c.Checksum)), StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task KeepCopyAsync(string id, string sha256, LocationCopy copy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _copies.Upsert(new CopyDocument { Id = Key(id, sha256), Location = id, Sha256 = sha256, Name = copy.Name, ChecksumKind = copy.Checksum?.Kind ?? default, Checksum = copy.Checksum?.Value });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ForgetCopyAsync(string id, string sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _copies.Delete(Key(id, sha256));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountAtAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_rules.FindById(Rule)?.Value ?? 1);
    }

    /// <inheritdoc />
    public Task SaveCountAtAsync(int locations, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _rules.Upsert(new RuleDocument { Id = Rule, Value = locations });
        return Task.CompletedTask;
    }

    private static string Key(string id, string sha256) => $"{id}:{sha256}";

    private sealed class LocationDocument
    {
        /// <summary>The location's id.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>What sort of place it is.</summary>
        public LocationKind Kind { get; set; }
        /// <summary>What it is called.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Backups go to it.</summary>
        public bool On { get; set; }
        /// <summary>The account or user it shows.</summary>
        public string? Account { get; set; }
        /// <summary>How it is reached, less its secrets.</summary>
        public Dictionary<string, string> Settings { get; set; } = [];
        /// <summary>When it was last turned on or backed up to.</summary>
        public DateTimeOffset? LastUsed { get; set; }
        /// <summary>When a backup to it last finished.</summary>
        public DateTimeOffset? LastBackup { get; set; }
        /// <summary>When its copies were last checked.</summary>
        public DateTimeOffset? LastChecked { get; set; }
        /// <summary>Why its last backup or check stopped.</summary>
        public string? Problem { get; set; }
    }

    private sealed class CopyDocument
    {
        /// <summary>The location's id and the file's checksum, together.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The location.</summary>
        public string Location { get; set; } = string.Empty;
        /// <summary>The file's SHA-256.</summary>
        public string Sha256 { get; set; } = string.Empty;
        /// <summary>Its name in the location's folder.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Which checksum the location keeps of it.</summary>
        public ChecksumKind ChecksumKind { get; set; }
        /// <summary>That checksum, as it was when checked; null when it was read back.</summary>
        public string? Checksum { get; set; }
    }

    private sealed class RuleDocument
    {
        /// <summary>The one record: "count-at".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>On how many locations a copy must be checked.</summary>
        public int Value { get; set; }
    }
}
