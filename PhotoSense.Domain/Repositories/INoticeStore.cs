namespace PhotoSense.Domain.Repositories;

/// <summary>Which notifications the person turned on, and when the last low-storage alert was sent.</summary>
/// <param name="LowStorage">Low-storage alerts are on.</param>
/// <param name="TrialReminder">The reminder two days before a free trial ends is on.</param>
/// <param name="LastLowStorage">When the last low-storage alert was sent; null before the first.</param>
public sealed record NoticeSettings(bool LowStorage, bool TrialReminder, DateTimeOffset? LastLowStorage)
{
    /// <summary>Nothing turned on, and nothing sent: notifications are off until the person turns them on.</summary>
    public static NoticeSettings None { get; } = new(false, false, null);
}

/// <summary>What a phone keeps of its notifications.</summary>
public interface INoticeStore
{
    /// <summary>The settings kept, or <see cref="NoticeSettings.None"/> before any were.</summary>
    Task<NoticeSettings> GetAsync(CancellationToken ct);

    /// <summary>Keeps <paramref name="settings"/> in place of those before.</summary>
    Task SaveAsync(NoticeSettings settings, CancellationToken ct);
}
