using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>Scans' records kept in memory.</summary>
public class InMemoryPhotoRepositoryTests
{
    [Test]
    public async Task Add_Get_Update_Delete_Works(CancellationToken ct)
    {
        var repo = new InMemoryPhotoRepository();
        var photo = new Photo{ SourcePath="a", FileName="f.jpg", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="H" };
        await repo.AddOrUpdateAsync(photo, ct);
        var fetched = await repo.GetAsync(photo.Id, ct);
        await Assert.That(fetched).IsNotNull();
        fetched!.ContentHash = "H2";
        await repo.AddOrUpdateAsync(fetched, ct);
        var byHash = await repo.GetByHashAsync("h2", ct); // case-insensitive
        byHash.Single();
        await repo.DeleteAsync(photo.Id, ct);
        var missing = await repo.GetAsync(photo.Id, ct);
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Counts_Them(CancellationToken ct)
    {
        var repo = new InMemoryPhotoRepository();
        await Assert.That(await repo.ClearAsync(ct)).IsEqualTo(0);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "a.jpg", FileName = "a.jpg" }, ct);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "b.jpg", FileName = "b.jpg" }, ct);
        var version = repo.Version;

        await Assert.That(await repo.ClearAsync(ct)).IsEqualTo(2);

        await Assert.That(await repo.GetAllAsync(ct)).IsEmpty();
        await Assert.That(await repo.GetByPathAsync("a.jpg", ct)).IsNull();
        await Assert.That(repo.Version).IsNotEqualTo(version);      // whatever was worked out from the records is out of date
    }
}
