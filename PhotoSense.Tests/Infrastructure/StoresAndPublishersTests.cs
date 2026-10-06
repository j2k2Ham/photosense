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
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

// InMemoryScanLogSink feeds one process-wide queue, so this class must not run beside the others that read it.
[Collection("ScanLogQueue")]
public sealed class StoresAndPublishersTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, Path.ChangeExtension(_db, null) + "-log.db" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Fact]
    public async Task The_Audit_Trail_Lists_The_Newest_Entries_First()
    {
        using var repo = new LiteDbAuditRepository(_db);
        await repo.AddAsync(new AuditEntry { Action = "Keep", UtcTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await repo.AddAsync(new AuditEntry { Action = "Delete", UtcTimestamp = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        await repo.AddAsync(new AuditEntry { Action = "Move", UtcTimestamp = new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Equal(new[] { "Move", "Delete", "Keep" }, (await repo.RecentAsync()).Select(a => a.Action));
        Assert.Equal(new[] { "Move", "Delete" }, (await repo.RecentAsync(take: 2)).Select(a => a.Action));
    }

    [Fact]
    public async Task An_Audit_Trail_On_A_Shared_Database_Leaves_It_Open_For_Its_Other_Users()
    {
        using var shared = new LiteDatabase(_db);
        var repo = new LiteDbAuditRepository(shared);
        await repo.AddAsync(new AuditEntry { Action = "Keep" });
        repo.Dispose();
        repo.Dispose(); // a second time does nothing

        Assert.Equal(1, shared.GetCollection<AuditEntry>("audit").Count());

        var own = new LiteDbAuditRepository(Path.ChangeExtension(_db, ".own.db"));
        own.Dispose();
        own.Dispose();
        File.Delete(Path.ChangeExtension(_db, ".own.db"));
    }

    [Fact]
    public void A_Scans_Log_Keeps_Only_Its_Most_Recent_Lines()
    {
        var sink = new InMemoryScanLogSink();
        for (int i = 0; i < 450; i++) sink.Log("scan-1", "Info", $"line {i}");

        Assert.Equal(200, sink.GetRecent("scan-1").Count);              // the default page
        Assert.Equal("line 449", sink.GetRecent("scan-1")[^1].message);
        var kept = sink.GetRecent("scan-1", max: 1000);
        Assert.Equal((400, "line 50"), (kept.Count, kept[0].message));    // older lines were dropped
        Assert.Empty(sink.GetRecent("another-scan"));
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    [Fact]
    public async Task Events_Are_Written_To_The_Outbox_With_Their_Type_And_Content()
    {
        OutboxMessage? written = null;
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Callback<OutboxMessage, CancellationToken>((m, _) => written = m).Returns(Task.CompletedTask);
        var id = PhotoId.New();

        await new OutboxIntegrationEventPublisher(store.Object).PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), DateTime.UtcNow, id, "a.jpg"));

        Assert.Equal(typeof(PhotoDeletedEvent).FullName, written!.Type);
        Assert.Contains("a.jpg", written.Payload);
        Assert.Null(written.ProcessedUtc);
    }

    [Fact]
    public void An_Event_Is_Filed_Under_Its_Full_Name_Or_Its_Short_One_Where_It_Has_No_Other()
    {
        Assert.Equal("PhotoSense.Domain.Events.PhotoDeletedEvent", OutboxIntegrationEventPublisher.NameOf(typeof(PhotoDeletedEvent)));
        // A type that stands for "whatever is supplied later" has no full name.
        var placeholder = typeof(List<>).GetGenericArguments()[0];
        Assert.Null(placeholder.FullName);
        Assert.Equal("T", OutboxIntegrationEventPublisher.NameOf(placeholder));
    }

    [Fact]
    public async Task The_In_Memory_Publisher_Holds_What_Was_Published()
    {
        var evt = new PhotoDeletedEvent(Guid.NewGuid(), DateTime.UtcNow, PhotoId.New(), "b.jpg");
        await new InMemoryIntegrationEventPublisher().PublishAsync(evt);
        Assert.Contains(evt, InMemoryIntegrationEventPublisher.Drain());
    }

    [Fact]
    public void Messaging_Has_Usable_Defaults()
    {
        var options = new MessagingOptions();
        Assert.Equal((string.Empty, "photosense-events"), (options.ServiceBusConnection, options.TopicName));
        options.ServiceBusConnection = "Endpoint=sb://x";
        options.TopicName = "other";
        Assert.Equal(("Endpoint=sb://x", "other"), (options.ServiceBusConnection, options.TopicName));
    }
}
