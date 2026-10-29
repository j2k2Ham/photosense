using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The devices this one is paired with, and its own id, kept in the database with everything else.</summary>
public sealed class LiteDbPairingStore : IPairingStore
{
    private readonly ILiteCollection<PairingDocument> _paired;
    private readonly ILiteCollection<SelfDocument> _self;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbPairingStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<PairingDocument>();
        LiteDbMapping.Prepare<SelfDocument>();
        _paired = db.GetCollection<PairingDocument>("paired_devices");
        _self = db.GetCollection<SelfDocument>("transfer_self");
    }

    public Task<string> SelfIdAsync()
    {
        lock (_self)
        {
            if (_self.FindById("self") is { } self) return Task.FromResult(self.DeviceId);
            var made = new SelfDocument { Id = "self", DeviceId = Guid.NewGuid().ToString("N") };
            _self.Insert(made);
            return Task.FromResult(made.DeviceId);
        }
    }

    public Task<PairedDevice?> GetAsync(string id)
        => Task.FromResult(_paired.FindById(id) is { } found ? new PairedDevice(found.Id, found.Name, found.Key, found.PairedUtc) : null);

    public Task SaveAsync(PairedDevice device)
    {
        _paired.Upsert(new PairingDocument { Id = device.Id, Name = device.Name, Key = device.Key, PairedUtc = device.PairedUtc });
        return Task.CompletedTask;
    }

    private sealed class PairingDocument
    {
        [BsonId] public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte[] Key { get; set; } = [];
        public DateTime PairedUtc { get; set; }
    }

    private sealed class SelfDocument
    {
        [BsonId] public string Id { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }
}
