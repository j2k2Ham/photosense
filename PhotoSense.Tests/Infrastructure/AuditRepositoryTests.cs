using PhotoSense.Infrastructure.Persistence;
using System.IO;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Infrastructure;

public class AuditRepositoryTests
{
    [Test]
    public async Task Add_And_Retrieve()
    {
        var db = Path.GetTempFileName();
        var repo = new LiteDbAuditRepository(db);
        await repo.AddAsync(new AuditEntry{ Action="Keep", PhotoId="p1", Details=""});
        var list = await repo.RecentAsync();
        await Assert.That(list).HasSingleItem();
        await Assert.That(list[0].Action).IsEqualTo("Keep");
    }
}