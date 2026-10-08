using PhotoSense.Application.Photos.Interfaces;

namespace PhotoSense.Tests.Domain;

public class RecordsSmokeTests
{
    [Test]
    public async Task PagedResult_TotalPages_Computed()
    {
        var pr = new PagedResult<int>(new List<int>{1,2,3}, 1, 2, 3);
        await Assert.That(pr.TotalPages).IsEqualTo(2);
    }
}
