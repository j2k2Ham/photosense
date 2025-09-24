using PhotoSense.Infrastructure.Hashing;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class PerceptualHashingServiceTests
{
    [Fact]
    public async Task Hashes_Stream_Consistently()
    {
        var svc = new PerceptualHashingService();
        await using var ms1 = new MemoryStream(new byte[]{ 1,2,3,4,5,6,7,8 });
        await using var ms2 = new MemoryStream(new byte[]{ 1,2,3,4,5,6,7,8 });
        var h1 = await svc.ComputeHashAsync(ms1);
        var h2 = await svc.ComputeHashAsync(ms2);
        Assert.Equal(h1, h2);
    }
}
