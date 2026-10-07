using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class InMemoryPhotoRepositoryTests
{
    [Fact]
    public async Task Add_Get_Update_Delete_Works()
    {
        var repo = new InMemoryPhotoRepository();
        var photo = new Photo{ SourcePath="a", FileName="f.jpg", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="H" };
        await repo.AddOrUpdateAsync(photo);
        var fetched = await repo.GetAsync(photo.Id);
        Assert.NotNull(fetched);
        fetched!.ContentHash = "H2";
        await repo.AddOrUpdateAsync(fetched);
        var byHash = await repo.GetByHashAsync("h2"); // case-insensitive
        Assert.Single(byHash);
        await repo.DeleteAsync(photo.Id);
        var missing = await repo.GetAsync(photo.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Clearing_Forgets_Every_Record_And_Counts_Them()
    {
        var repo = new InMemoryPhotoRepository();
        Assert.Equal(0, await repo.ClearAsync());
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "a.jpg", FileName = "a.jpg" });
        await repo.AddOrUpdateAsync(new Photo { SourcePath = "b.jpg", FileName = "b.jpg" });
        var version = repo.Version;

        Assert.Equal(2, await repo.ClearAsync());

        Assert.Empty(await repo.GetAllAsync());
        Assert.Null(await repo.GetByPathAsync("a.jpg"));
        Assert.NotEqual(version, repo.Version);      // whatever was worked out from the records is out of date
    }
}
