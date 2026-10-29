using LiteDB;
using Moq;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Events;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Tests.Infrastructure;

// InMemoryScanLogSink feeds one process-wide queue, so this class must not run beside the others that read it.
[NotInParallel("ScanLogQueue")]
public sealed class StoresAndPublishersTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, Path.ChangeExtension(_db, null) + "-log.db" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Test]
    public async Task The_Audit_Trail_Lists_The_Newest_Entries_First()
    {
        using var repo = new LiteDbAuditRepository(_db);
        await repo.AddAsync(new AuditEntry { Action = "Keep", UtcTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await repo.AddAsync(new AuditEntry { Action = "Delete", UtcTimestamp = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        await repo.AddAsync(new AuditEntry { Action = "Move", UtcTimestamp = new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc) });

        await Assert.That((await repo.RecentAsync()).Select(a => a.Action)).IsEquivalentTo(new[] { "Move", "Delete", "Keep" }, CollectionOrdering.Matching);
        await Assert.That((await repo.RecentAsync(take: 2)).Select(a => a.Action)).IsEquivalentTo(new[] { "Move", "Delete" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Audit_Trail_On_A_Shared_Database_Leaves_It_Open_For_Its_Other_Users()
    {
        using var shared = new LiteDatabase(_db);
        var repo = new LiteDbAuditRepository(shared);
        await repo.AddAsync(new AuditEntry { Action = "Keep" });
        repo.Dispose();
        repo.Dispose(); // a second time does nothing

        await Assert.That(shared.GetCollection<AuditEntry>("audit").Count()).IsEqualTo(1);

        var own = new LiteDbAuditRepository(Path.ChangeExtension(_db, ".own.db"));
        own.Dispose();
        own.Dispose();
        File.Delete(Path.ChangeExtension(_db, ".own.db"));
    }

    [Test]
    public async Task A_Scans_Log_Keeps_Only_Its_Most_Recent_Lines()
    {
        var sink = new InMemoryScanLogSink();
        for (int i = 0; i < 450; i++) sink.Log("scan-1", "Info", $"line {i}");

        await Assert.That(sink.GetRecent("scan-1").Count).IsEqualTo(200);              // the default page
        await Assert.That(sink.GetRecent("scan-1")[^1].message).IsEqualTo("line 449");
        var kept = sink.GetRecent("scan-1", max: 1000);
        await Assert.That((kept.Count, kept[0].message)).IsEqualTo((400, "line 50"));    // older lines were dropped
        await Assert.That(sink.GetRecent("another-scan")).IsEmpty();
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    [Test]
    public async Task Events_Are_Written_To_The_Outbox_With_Their_Type_And_Content()
    {
        OutboxMessage? written = null;
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Callback<OutboxMessage, CancellationToken>((m, _) => written = m).Returns(Task.CompletedTask);
        var id = PhotoId.New();

        await new OutboxIntegrationEventPublisher(store.Object).PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), DateTime.UtcNow, id, "a.jpg"));

        await Assert.That(written!.Type).IsEqualTo(typeof(PhotoDeletedEvent).FullName);
        await Assert.That(written.Payload).Contains("a.jpg");
        await Assert.That(written.ProcessedUtc).IsNull();
    }

    [Test]
    public async Task An_Event_Is_Filed_Under_Its_Full_Name_Or_Its_Short_One_Where_It_Has_No_Other()
    {
        await Assert.That(OutboxIntegrationEventPublisher.NameOf(typeof(PhotoDeletedEvent))).IsEqualTo("PhotoSense.Domain.Events.PhotoDeletedEvent");
        // A type that stands for "whatever is supplied later" has no full name.
        var placeholder = typeof(List<>).GetGenericArguments()[0];
        await Assert.That(placeholder.FullName).IsNull();
        await Assert.That(OutboxIntegrationEventPublisher.NameOf(placeholder)).IsEqualTo("T");
    }

    [Test]
    public async Task Messaging_Has_Usable_Defaults()
    {
        var options = new MessagingOptions();
        await Assert.That((options.ServiceBusConnection, options.TopicName)).IsEqualTo((string.Empty, "photosense-events"));
        options.ServiceBusConnection = "Endpoint=sb://x";
        options.TopicName = "other";
        await Assert.That((options.ServiceBusConnection, options.TopicName)).IsEqualTo(("Endpoint=sb://x", "other"));
    }
}
