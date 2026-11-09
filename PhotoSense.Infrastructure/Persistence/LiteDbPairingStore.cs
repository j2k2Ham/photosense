using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The devices this one is paired with, and its own id, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbPairingStore : IPairingStore
{
    private readonly ILiteCollection<PairingDocument> _paired;
    private readonly ILiteCollection<SelfDocument> _self;
    private readonly TimeProvider _time;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    /// <param name="time">The clock each pairing is stamped from.</param>
    public LiteDbPairingStore(LiteDatabase db, TimeProvider time)
    {
        LiteDbMapping.Prepare<PairingDocument>();
        LiteDbMapping.Prepare<SelfDocument>();
        _paired = db.GetCollection<PairingDocument>("paired_devices");
        _self = db.GetCollection<SelfDocument>("transfer_self");
        _time = time;
    }

    /// <inheritdoc />
    public Task<string> SelfIdAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_self)
        {
            if (_self.FindById("self") is { } self) return Task.FromResult(self.DeviceId);
            var made = new SelfDocument { Id = "self", DeviceId = Guid.NewGuid().ToString("N") };
            _self.Insert(made);
            return Task.FromResult(made.DeviceId);
        }
    }

    /// <inheritdoc />
    public Task<PairedDevice?> GetAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_paired.FindById(id) is { } found ? new PairedDevice(found.Id, found.Name, found.Key) : null);
    }

    /// <inheritdoc />
    public Task SaveAsync(PairedDevice device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _paired.Upsert(new PairingDocument { Id = device.Id, Name = device.Name, Key = device.Key, PairedUtc = _time.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }

    private sealed class PairingDocument
    {
        /// <summary>The other device's id.</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>What the other device is called.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>The secret the two share.</summary>
        public byte[] Key { get; set; } = [];
        /// <summary>When the two were paired; kept for whoever looks into the database, though nothing reads it yet.</summary>
        public DateTime PairedUtc { get; set; }
    }

    private sealed class SelfDocument
    {
        /// <summary>The one record of this device: "self".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>This device's own id.</summary>
        public string DeviceId { get; set; } = string.Empty;
    }
}
