using LiteDB;
using Trovolve.Domain.Repositories;

namespace Trovolve.Infrastructure.Persistence;

/// <summary>A phone's notification settings, kept in the database with everything else.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbNoticeStore : INoticeStore
{
    private const string Key = "notices";
    private readonly ILiteCollection<NoticeDocument> _notices;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    public LiteDbNoticeStore(LiteDatabase db)
    {
        LiteDbMapping.Prepare<NoticeDocument>();
        _notices = db.GetCollection<NoticeDocument>("notices");
    }

    /// <inheritdoc />
    public Task<NoticeSettings> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_notices.FindById(Key) is { } d
            ? new NoticeSettings(d.LowStorage, d.TrialReminder, d.LastLowStorageUtcTicks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null)
            : NoticeSettings.None);
    }

    /// <inheritdoc />
    public Task SaveAsync(NoticeSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _notices.Upsert(new NoticeDocument
        {
            Id = Key, LowStorage = settings.LowStorage, TrialReminder = settings.TrialReminder, LastLowStorageUtcTicks = settings.LastLowStorage?.UtcTicks,
        });
        return Task.CompletedTask;
    }

    private sealed class NoticeDocument
    {
        /// <summary>The one record: "notices".</summary>
        [BsonId] public string Id { get; set; } = string.Empty;
        /// <summary>Low-storage alerts are on.</summary>
        public bool LowStorage { get; set; }
        /// <summary>The trial's reminder is on.</summary>
        public bool TrialReminder { get; set; }
        /// <summary>When the last low-storage alert was sent, in UTC ticks.</summary>
        public long? LastLowStorageUtcTicks { get; set; }
    }
}
