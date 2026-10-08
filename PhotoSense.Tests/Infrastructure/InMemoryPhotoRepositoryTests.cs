using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public class InMemoryPhotoRepositoryTests
{
    [Test]
    public async Task Add_Get_Update_Delete_Works()
    {
        var repo = new InMemoryPhotoRepository();
        var photo = new Photo{ SourcePath="a", FileName="f.jpg", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="H" };
        await repo.AddOrUpdateAsync(photo);
        var fetched = await repo.GetAsync(photo.Id);
        await Assert.That(fetched).IsNotNull();
        fetched!.ContentHash = "H2";
        await repo.AddOrUpdateAsync(fetched);
        var byHash = await repo.GetByHashAsync("h2"); // case-insensitive
        byHash.Single();
        await repo.DeleteAsync(photo.Id);
        var missing = await repo.GetAsync(photo.Id);
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Counts_Them()
    {
        var repo = new InMemoryPhotoRepository();
        await Assert.That(await repo.ClearAsync()).IsEqualTo(0);
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "a.jpg", FileName = "a.jpg" });
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "b.jpg", FileName = "b.jpg" });
        var version = repo.Version;

        await Assert.That(await repo.ClearAsync()).IsEqualTo(2);

        await Assert.That(await repo.GetAllAsync()).IsEmpty();
        await Assert.That(await repo.GetByPathAsync("a.jpg")).IsNull();
        await Assert.That(repo.Version).IsNotEqualTo(version);      // whatever was worked out from the records is out of date
    }
}
