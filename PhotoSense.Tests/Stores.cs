using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Infrastructure;

namespace PhotoSense.Tests;

/// <summary>
/// The stores every program keeps, made as the programs make them (<c>AddPhotoSenseCore</c>), on a database the test
/// holds and disposes.
/// </summary>
internal static class Stores
{
    // The program's registrations, with the test's database in place of the one its settings would open.
    private static IServiceProvider On(LiteDatabase db, TimeProvider? time = null)
    {
        var services = new ServiceCollection().AddSingleton(Options.Create(new PhotoStorageOptions())).AddPhotoSenseCore().AddSingleton(db);
        if (time is not null) services.AddSingleton(time);
        return services.BuildServiceProvider();
    }

    /// <summary>Scans' records of each file.</summary>
    public static IPhotoRepository Photos(this LiteDatabase db) => On(db).GetRequiredService<IPhotoRepository>();
    /// <summary>The record of what was done to photos, each entry stamped from <paramref name="time"/>.</summary>
    public static IAuditRepository Audit(this LiteDatabase db, TimeProvider time) => On(db, time).GetRequiredService<IAuditRepository>();
    /// <summary>The record of scans run.</summary>
    public static IScanHistory ScanHistory(this LiteDatabase db) => On(db).GetRequiredService<IScanHistory>();
    /// <summary>The changes kept so that they can be undone.</summary>
    public static IOrganizeBatchStore Batches(this LiteDatabase db) => On(db).GetRequiredService<IOrganizeBatchStore>();
    /// <summary>The devices paired with this one, each stamped from <paramref name="time"/>.</summary>
    public static IPairingStore Pairings(this LiteDatabase db, TimeProvider time) => On(db, time).GetRequiredService<IPairingStore>();
    /// <summary>What has been read from each file for Organize.</summary>
    public static IMediaDetailsStore MediaDetails(this LiteDatabase db) => On(db).GetRequiredService<IMediaDetailsStore>();
}
