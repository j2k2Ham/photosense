using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Plans;
using PhotoSense.Infrastructure;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Tests;

/// <summary>A phone that says of itself what the test says: what sort it is, whether it is charging, its storage, and whether iCloud keeps its photos.</summary>
internal sealed class FakeDevice : IDeviceStatus
{
    /// <inheritdoc />
    public string Kind { get; set; } = "iPhone";

    /// <inheritdoc />
    public bool Charging { get; set; }

    /// <inheritdoc />
    public DeviceStorage Storage { get; set; } = new(128_000_000_000, 16_000_000_000);

    /// <inheritdoc />
    public bool CloudPhotos { get; set; }
}

/// <summary>The favorites in a phone's photos app, as the test lists them.</summary>
internal sealed class FakeFavorites : IFavoritePhotos
{
    /// <summary>The favorites' paths.</summary>
    public HashSet<string> Paths { get; } = [];

    /// <inheritdoc />
    public void SetFavorite(string path, bool favorite)
    {
        if (favorite) Paths.Add(path);
        else Paths.Remove(path);
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> FavoritesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<string>>(Paths.ToHashSet());
    }
}

/// <summary>A computer's keeping of phones' backups, for tests that make their services themselves.</summary>
internal static class TestBackups
{
    /// <summary>
    /// Backups kept in folders of <paramref name="root"/> on this computer's disk, the person's own folder being
    /// <paramref name="home"/>, with no limit on removals and the phones kept in <paramref name="store"/>.
    /// </summary>
    public static ComputerBackup Computer(string root, IBackupStore? store = null, TimeProvider? clock = null, string? home = null, IPhotoFileSystem? files = null, Subscription? plan = null)
    {
        var disk = files ?? new DiskFileSystem();
        var changes = new FileChanges(new MemoryBatchStore(), new InMemoryPhotoRepository(), Moq.Mock.Of<IAuditRepository>(), disk, TestPlans.Unlimited());
        // The computer's own readers of what a file says and where places are, as AddPhotoSenseCore makes them.
        var core = new ServiceCollection().AddSingleton(disk).AddPhotoSenseApplication().AddPhotoSenseCore().AddStandInStores().BuildServiceProvider();
        return new ComputerBackup(store ?? new MemoryBackupStore(), disk, changes, plan ?? TestPlans.Subscription(), root, home ?? Path.GetDirectoryName(root)!, clock ?? new TestClock(),
            core.GetRequiredService<IMediaDetailsReader>(), core.GetRequiredService<IPlaceNameResolver>());
    }
}
