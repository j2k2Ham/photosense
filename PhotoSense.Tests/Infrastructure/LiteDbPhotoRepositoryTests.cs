using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class LiteDbPhotoRepositoryTests
{
    [Fact]
    public async Task RoundTrip_Persists_And_Maps_Categories()
    {
        var tempDb = Path.GetTempFileName();
        try
        {
            using var repo = new LiteDbPhotoRepository(tempDb);
            var photo = new Photo{ SourcePath="x", FileName="y.jpg", FileSizeBytes=1, Set=PhotoSet.Secondary, ContentHash="ABC", PerceptualHash="FFFF" };
            photo.AddCategory("Nature");
            await repo.AddOrUpdateAsync(photo);
            var all = await repo.GetAllAsync();
            Assert.Single(all);
            Assert.Contains("Nature", all[0].Categories);
            var byId = await repo.GetAsync(photo.Id);
            Assert.NotNull(byId);
            var byHash = await repo.GetByHashAsync("ABC");
            Assert.Single(byHash);
            await repo.DeleteAsync(photo.Id);
            var empty = await repo.GetAllAsync();
            Assert.Empty(empty);
        }
        finally { File.Delete(tempDb); }
    }
}
