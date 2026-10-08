using LiteDB;
using PhotoSense.Domain.Events;
using PhotoSense.Infrastructure.Events;

namespace PhotoSense.Tests.Infrastructure.Outbox;

public class LiteDbOutboxStoreTests
{
    private LiteDbOutboxStore CreateStore(out LiteDatabase db)
    {
        db = new LiteDatabase(new MemoryStream());
        return new LiteDbOutboxStore(db);
    }

    [Test]
    public async Task Add_And_GetUnprocessed_Returns_Message()
    {
        var store = CreateStore(out _);
        var msg = new OutboxMessage { Type = "Test", Payload = "{}", OccurredUtc = DateTime.UtcNow.AddMinutes(-1) };
        await store.AddAsync(msg);
        var list = await store.GetUnprocessedAsync();
        await Assert.That(list).HasSingleItem();
        await Assert.That(list[0].Id).IsEqualTo(msg.Id);
    }

    [Test]
    public async Task MarkProcessed_Removes_From_Unprocessed()
    {
        var store = CreateStore(out _);
        var msg = new OutboxMessage { Type = "Test", Payload = "{}", OccurredUtc = DateTime.UtcNow };
        await store.AddAsync(msg);
        await store.MarkProcessedAsync(msg.Id);
        var list = await store.GetUnprocessedAsync();
        await Assert.That(list).IsEmpty();
    }

    [Test]
    public async Task Marking_A_Message_That_Is_Not_There_Changes_Nothing()
    {
        var store = CreateStore(out _);
        var msg = new OutboxMessage { Type = "Test", Payload = "{}", OccurredUtc = DateTime.UtcNow };
        await store.AddAsync(msg);
        await store.MarkProcessedAsync(Guid.NewGuid());
        await Assert.That((await store.GetUnprocessedAsync()).Single().Id).IsEqualTo(msg.Id);
    }

    [Test]
    public async Task Orders_By_OccurredUtc()
    {
        var store = CreateStore(out _);
        var older = new OutboxMessage { Type = "A", Payload = "{}", OccurredUtc = DateTime.UtcNow.AddMinutes(-10) };
        var newer = new OutboxMessage { Type = "B", Payload = "{}", OccurredUtc = DateTime.UtcNow };
        await store.AddAsync(newer);
        await store.AddAsync(older);
        var list = await store.GetUnprocessedAsync();
        await Assert.That(list[0].Id).IsEqualTo(older.Id);
    }
}
