using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The home chosen for Trips, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbTripStore : ITripStore
{
    private const string Key = "home";
    private readonly ILiteCollection<HomeDocument> _home;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbTripStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<HomeDocument>();
        _home = db.GetCollection<HomeDocument>("trip_home");
    }

    /// <inheritdoc />
    public Task<HomeTown?> HomeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_home.FindById(Key) is { } d ? new HomeTown(d.Town, d.Region, d.Country, d.Latitude, d.Longitude) : null);
    }

    /// <inheritdoc />
    public Task SaveHomeAsync(HomeTown? home, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (home is null) _home.Delete(Key);
        else _home.Upsert(new HomeDocument { Id = Key, Town = home.Town, Region = home.Region, Country = home.Country, Latitude = home.Latitude, Longitude = home.Longitude });
        return Task.CompletedTask;
    }

    private sealed class HomeDocument
    {
        /// <summary>The one record: "home".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>The town.</summary>
        public string Town { get; set; } = string.Empty;
        /// <summary>Its state, province or county.</summary>
        public string Region { get; set; } = string.Empty;
        /// <summary>Its country.</summary>
        public string Country { get; set; } = string.Empty;
        /// <summary>Degrees north.</summary>
        public double Latitude { get; set; }
        /// <summary>Degrees east.</summary>
        public double Longitude { get; set; }
    }
}
